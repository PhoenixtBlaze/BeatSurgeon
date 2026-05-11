using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class PatreonAuthManager : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("PatreonAuthManager");
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private const string BackendBaseUrl = "https://phoenixblaze0.duckdns.org";
        private const string PatreonIdentityUrl = "https://www.patreon.com/api/oauth2/v2/identity?fields[user]=full_name";

        private static PatreonAuthManager _instance;
        internal static PatreonAuthManager Instance => _instance ?? (_instance = new PatreonAuthManager());

        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        private string _accessToken;
        private string _refreshToken;
        private Timer _refreshTimer;
        private CancellationTokenSource _loginCts;
        private volatile bool _loginInProgress;
        private volatile bool _authReadyRaised;

        public event Action OnTokensUpdated;
        public event Action OnIdentityUpdated;
        public event Action OnAuthReady;
        public event Action OnReauthRequired;

        internal string UserId { get; private set; }
        internal string UserName { get; private set; }

        internal bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(_accessToken) &&
            ReadTokenExpiryUtc() > DateTime.UtcNow.AddMinutes(1);

        internal bool IsReauthRequired => PluginConfig.Instance?.PatreonReauthRequired == true;

        [Inject]
        public PatreonAuthManager()
        {
            _instance = this;
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize");

            LoadTokens();
            RestoreIdentityFromCache();

            _ = Task.Run(async () =>
            {
                try
                {
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        if (PluginConfig.Instance.HasValidPatreonToken)
                        {
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(PluginConfig.Instance?.PatreonEncryptedAccessToken))
                        {
                            break;
                        }

                        _log.Auth($"Config not ready yet (attempt {attempt + 1}/5) - retrying in 200 ms...");
                        await Task.Delay(200).ConfigureAwait(false);
                        LoadTokens();
                        RestoreIdentityFromCache();
                    }

                    if (!PluginConfig.Instance.HasValidPatreonToken && !string.IsNullOrWhiteSpace(_refreshToken))
                    {
                        _log.Auth("StartupRefresh", "Patreon access token missing or expired - refreshing from saved refresh token");
                        try
                        {
                            await RefreshTokenAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _log.Exception(ex, "StartupRefresh");
                        }
                    }

                    if (PluginConfig.Instance.HasValidPatreonToken)
                    {
                        _log.Auth("ValidTokenFound - scheduling proactive refresh");
                        ScheduleProactiveRefresh();
                        await BootstrapIdentityAndEntitlementsAsync(CancellationToken.None).ConfigureAwait(false);
                        if (!IsReauthRequired)
                        {
                            RaiseAuthReadyIfPossible();
                        }
                    }
                    else
                    {
                        _log.Auth("NoValidToken - Patreon OAuth will be triggered from the support dialog");
                    }
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "Initialize bootstrap");
                }
            });
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose - stopping Patreon token refresh timer");
            try
            {
                _refreshTimer?.Dispose();
                _loginCts?.Cancel();
                _loginCts?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Dispose");
            }
        }

        internal async Task<string> GetAccessTokenAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!PluginConfig.Instance.HasValidPatreonToken || string.IsNullOrWhiteSpace(_accessToken))
            {
                _log.Auth("GetAccessToken - no valid Patreon token, refreshing");
                await RefreshTokenAsync(ct).ConfigureAwait(false);
            }

            return _accessToken ?? string.Empty;
        }

        internal async Task<string> GetUserIdAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(UserId))
            {
                return UserId;
            }

            await EnsureIdentityAsync(ct).ConfigureAwait(false);
            return UserId ?? string.Empty;
        }

        internal async Task<bool> EnsureReadyAsync(CancellationToken ct = default(CancellationToken))
        {
            await GetAccessTokenAsync(ct).ConfigureAwait(false);
            await BootstrapIdentityAndEntitlementsAsync(ct).ConfigureAwait(false);
            RaiseAuthReadyIfPossible();

            return IsAuthenticated && !string.IsNullOrWhiteSpace(UserId);
        }

        internal async Task EnsureValidTokenAsync(CancellationToken ct = default(CancellationToken))
        {
            if (PluginConfig.Instance.HasValidPatreonToken && !string.IsNullOrWhiteSpace(_accessToken))
            {
                return;
            }

            await RefreshTokenAsync(ct).ConfigureAwait(false);
        }

        internal async Task InitiateLogin()
        {
            if (_loginInProgress)
            {
                PluginConfig.Instance.PatreonBackendStatus = "Login already in progress";
                return;
            }

            _loginInProgress = true;
            _loginCts?.Cancel();
            _loginCts = new CancellationTokenSource();

            try
            {
                string state = Guid.NewGuid().ToString("N");
                PluginConfig.Instance.PatreonBackendStatus = "Opening browser...";
                string loginUrl = BackendBaseUrl + "/patreon/login?state=" + Uri.EscapeDataString(state);
                Application.OpenURL(loginUrl);

                PluginConfig.Instance.PatreonBackendStatus = "Waiting for authorization...";
                await PollForBackendTokenAsync(state, _loginCts.Token).ConfigureAwait(false);
                await BootstrapIdentityAndEntitlementsAsync(_loginCts.Token).ConfigureAwait(false);
                ScheduleProactiveRefresh();
                RaiseAuthReadyIfPossible();

                PluginConfig.Instance.PatreonBackendStatus = "Connected";
                _log.Auth("LoginSucceeded");
            }
            catch (OperationCanceledException)
            {
                PluginConfig.Instance.PatreonBackendStatus = "Login cancelled";
                _log.Auth("LoginCancelled");
            }
            catch (Exception ex)
            {
                PluginConfig.Instance.PatreonBackendStatus = "Login failed";
                _log.Exception(ex, "InitiateLogin");
                throw;
            }
            finally
            {
                _loginInProgress = false;
            }
        }

        internal void Logout()
        {
            _accessToken = string.Empty;
            _refreshToken = string.Empty;
            UserId = string.Empty;
            UserName = string.Empty;
            _authReadyRaised = false;

            PersistTokenState();
            PluginConfig.Instance.CachedPatreonUserId = string.Empty;
            PluginConfig.Instance.CachedPatreonUserName = string.Empty;
            PluginConfig.Instance.PatreonBackendStatus = "Not connected";
            EntitlementsState.Clear(EntitlementProvider.Patreon);
            PluginConfig.Instance.CachedSupporterTier = (int)EntitlementsState.Current.Tier;
            ClearPatreonReauthRequired();
            PremiumVisualFeatureAccessController.SyncAllConfigEnabledStates();

            OnTokensUpdated?.Invoke();
            OnIdentityUpdated?.Invoke();
        }

        internal async Task ForceRefreshTokenAsync(CancellationToken ct = default(CancellationToken))
        {
            try
            {
                await RefreshTokenAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "ForceRefreshTokenAsync");
                MarkReauthRequired("Forced Patreon refresh failed: " + ex.GetType().Name);
            }
        }

        internal void ClearPatreonReauthRequired()
        {
            _log.Auth("ClearPatreonReauthRequired");
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg != null)
            {
                cfg.PatreonReauthRequired = false;
            }
        }

        internal void MarkReauthRequired(string reason)
        {
            if (IsReauthRequired)
            {
                return;
            }

            SetPatreonReauthRequired(reason);
        }

        private async Task PollForBackendTokenAsync(string state, CancellationToken ct)
        {
            int attempts = 0;
            const int maxAttempts = 90;

            while (attempts++ < maxAttempts)
            {
                ct.ThrowIfCancellationRequested();

                string url = BackendBaseUrl + "/patreon/token?state=" + Uri.EscapeDataString(state);
                HttpResponseMessage response;

                try
                {
                    response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    continue;
                }

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    JObject json = JObject.Parse(body);
                    string access = json["access_token"]?.ToString();
                    string refresh = json["refresh_token"]?.ToString();
                    int expiresIn = json["expires_in"]?.Value<int>() ?? 3600;

                    if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh))
                    {
                        throw new InvalidOperationException("Backend Patreon token response did not contain access/refresh tokens.");
                    }

                    await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        _accessToken = access;
                        _refreshToken = refresh;
                        WriteTokenExpiryUtc(DateTime.UtcNow.AddSeconds(expiresIn));
                        PersistTokenState();
                    }
                    finally
                    {
                        _tokenLock.Release();
                    }

                    ClearPatreonReauthRequired();
                    OnTokensUpdated?.Invoke();
                    return;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    continue;
                }

                throw new HttpRequestException("Backend /patreon/token failed: " + response.StatusCode + " body=" + body);
            }

            throw new TimeoutException("Timed out waiting for Patreon OAuth token handoff.");
        }

        private async Task EnsureIdentityAsync(CancellationToken ct = default(CancellationToken))
        {
            await EnsureValidTokenAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(UserId))
            {
                return;
            }

            if (await TryFetchIdentityAsync(_accessToken, ct).ConfigureAwait(false))
            {
                return;
            }

            _log.Auth("EnsureIdentity", "Patreon identity rejected token - refreshing");
            await RefreshTokenAsync(ct).ConfigureAwait(false);

            if (await TryFetchIdentityAsync(_accessToken, ct).ConfigureAwait(false))
            {
                return;
            }

            throw new HttpRequestException("Patreon identity fetch failed after token refresh.");
        }

        private async Task<bool> TryFetchIdentityAsync(string accessToken, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, PatreonIdentityUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        return false;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException("Patreon identity fetch failed: " + response.StatusCode + " body=" + body);
                    }

                    JObject json = JObject.Parse(body);
                    JToken user = json["data"];
                    if (user == null)
                    {
                        throw new InvalidOperationException("Patreon identity returned no user payload.");
                    }

                    UserId = user["id"]?.ToString() ?? string.Empty;
                    UserName = user["attributes"]?["full_name"]?.ToString() ?? string.Empty;

                    PluginConfig.Instance.CachedPatreonUserId = UserId;
                    PluginConfig.Instance.CachedPatreonUserName = UserName;
                    OnIdentityUpdated?.Invoke();
                    return true;
                }
            }
        }

        private async Task RefreshTokenAsync(CancellationToken ct)
        {
            _log.Auth("RefreshStarted");

            if (string.IsNullOrWhiteSpace(_refreshToken))
            {
                _log.Auth("RefreshSkipped", "No Patreon refresh token available");
                return;
            }

            await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string url = BackendBaseUrl + "/patreon/refresh";
                string jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(new { refresh_token = _refreshToken });
                using (var refreshRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                })
                using (HttpResponseMessage response = await _http.SendAsync(refreshRequest, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _log.Auth("RefreshFailed", "status=" + response.StatusCode);
                        throw new HttpRequestException("Patreon refresh failed: " + response.StatusCode + " body=" + body);
                    }

                    JObject json = JObject.Parse(body);
                    string access = json["access_token"]?.ToString();
                    string refresh = json["refresh_token"]?.ToString();
                    int expiresIn = json["expires_in"]?.Value<int>() ?? 3600;

                    if (string.IsNullOrWhiteSpace(access))
                    {
                        throw new InvalidOperationException("Patreon refresh response missing access_token.");
                    }

                    _accessToken = access;
                    if (!string.IsNullOrWhiteSpace(refresh))
                    {
                        _refreshToken = refresh;
                    }

                    WriteTokenExpiryUtc(DateTime.UtcNow.AddSeconds(expiresIn));
                    PersistTokenState();
                }

                _log.Auth("RefreshSucceeded");
                ClearPatreonReauthRequired();
                OnTokensUpdated?.Invoke();
                ScheduleProactiveRefresh();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RefreshTokenAsync");
                _log.Auth("RefreshFailed - Patreon entitlement checks will fail until user re-authenticates");
                SetPatreonReauthRequired("Patreon token refresh failed: " + ex.GetType().Name);
                throw;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private void ScheduleProactiveRefresh()
        {
            DateTime expiry = ReadTokenExpiryUtc();
            TimeSpan refreshAt = expiry - DateTime.UtcNow - TimeSpan.FromMinutes(5);

            if (refreshAt <= TimeSpan.Zero)
            {
                _log.Auth("TokenExpiredOrExpiringSoon - refreshing immediately");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RefreshTokenAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex, "ImmediateRefresh");
                        SetPatreonReauthRequired("Immediate Patreon token refresh failed");
                    }
                });
                return;
            }

            _log.Auth("SchedulingRefresh", $"refreshIn={refreshAt.TotalMinutes:F1}min expiresAt={expiry:u}");
            _refreshTimer?.Dispose();
            _refreshTimer = new Timer(async _ =>
            {
                _log.Auth("ProactiveRefreshTimerFired");
                try
                {
                    await RefreshTokenAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "ProactiveRefreshTimer");
                    SetPatreonReauthRequired("Proactive Patreon token refresh failed");
                }
            }, null, refreshAt, Timeout.InfiniteTimeSpan);
        }

        private void RaiseAuthReadyIfPossible()
        {
            if (_authReadyRaised) return;
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(UserId)) return;

            _authReadyRaised = true;
            _log.Auth("AuthReady", "patreonUserId=" + UserId);
            OnAuthReady?.Invoke();
        }

        private void SetPatreonReauthRequired(string reason)
        {
            _log.Auth("SetPatreonReauthRequired", reason);
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg != null)
            {
                cfg.PatreonReauthRequired = true;
            }

            EntitlementsState.Clear(EntitlementProvider.Patreon);
            if (cfg != null)
            {
                cfg.CachedSupporterTier = (int)EntitlementsState.Current.Tier;
            }

            PremiumVisualFeatureAccessController.SyncAllConfigEnabledStates();
            _authReadyRaised = false;
            OnReauthRequired?.Invoke();
        }

        private void LoadTokens()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            bool normalizeStoredAccessToken;
            bool normalizeStoredRefreshToken;

            _accessToken = ReadStoredToken(
                cfg.PatreonAccessToken,
                cfg.PatreonEncryptedAccessToken,
                out normalizeStoredAccessToken);
            _refreshToken = ReadStoredToken(
                cfg.PatreonRefreshToken,
                cfg.PatreonEncryptedRefreshToken,
                out normalizeStoredRefreshToken);

            if (normalizeStoredAccessToken || normalizeStoredRefreshToken)
            {
                _log.Auth("LoadTokens", "Normalizing stored Patreon tokens to encrypted-only config fields");
                PersistTokenState();
            }

            if (cfg.PatreonTokenExpiry == DateTime.MinValue && cfg.PatreonTokenExpiryTicks > 0)
            {
                cfg.PatreonTokenExpiry = new DateTime(cfg.PatreonTokenExpiryTicks, DateTimeKind.Utc);
            }
        }

        private void PersistTokenState()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            string encryptedAccessToken = EncryptString(_accessToken ?? string.Empty);
            string encryptedRefreshToken = EncryptString(_refreshToken ?? string.Empty);

            cfg.PatreonAccessToken = string.Empty;
            cfg.PatreonRefreshToken = string.Empty;
            cfg.PatreonEncryptedAccessToken = encryptedAccessToken;
            cfg.PatreonEncryptedRefreshToken = encryptedRefreshToken;
            cfg.PatreonTokenExpiryTicks = cfg.PatreonTokenExpiry.ToUniversalTime().Ticks;
        }

        private string ReadStoredToken(string primaryStoredToken, string encryptedStoredToken, out bool normalizeStoredToken)
        {
            normalizeStoredToken = false;

            if (!string.IsNullOrWhiteSpace(encryptedStoredToken) &&
                TryDecryptString(encryptedStoredToken, out string decryptedEncryptedToken))
            {
                if (!string.IsNullOrWhiteSpace(primaryStoredToken))
                {
                    normalizeStoredToken = true;
                }

                return decryptedEncryptedToken;
            }

            if (!string.IsNullOrWhiteSpace(primaryStoredToken))
            {
                if (TryDecryptString(primaryStoredToken, out string decryptedPrimaryToken))
                {
                    normalizeStoredToken = true;
                    return decryptedPrimaryToken;
                }

                normalizeStoredToken = true;
                return primaryStoredToken;
            }

            return string.Empty;
        }

        private void RestoreIdentityFromCache()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            UserId = cfg.CachedPatreonUserId ?? string.Empty;
            UserName = cfg.CachedPatreonUserName ?? string.Empty;
        }

        private async Task BootstrapIdentityAndEntitlementsAsync(CancellationToken ct)
        {
            await EnsureIdentityAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                return;
            }

            await PatreonApiClient.Instance.RefreshEntitlementsAsync(_accessToken, ct).ConfigureAwait(false);
        }

        private DateTime ReadTokenExpiryUtc()
        {
            DateTime expiry = PluginConfig.Instance.PatreonTokenExpiry;
            if (expiry == DateTime.MinValue && PluginConfig.Instance.PatreonTokenExpiryTicks > 0)
            {
                expiry = new DateTime(PluginConfig.Instance.PatreonTokenExpiryTicks, DateTimeKind.Utc);
                PluginConfig.Instance.PatreonTokenExpiry = expiry;
            }

            if (expiry.Kind == DateTimeKind.Unspecified)
            {
                expiry = DateTime.SpecifyKind(expiry, DateTimeKind.Utc);
            }

            return expiry.ToUniversalTime();
        }

        private void WriteTokenExpiryUtc(DateTime expiry)
        {
            PluginConfig.Instance.PatreonTokenExpiry = expiry.ToUniversalTime();
            PluginConfig.Instance.PatreonTokenExpiryTicks = PluginConfig.Instance.PatreonTokenExpiry.Ticks;
        }

        private string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }

            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(plainText);
                byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cipher);
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool TryDecryptString(string cipherText, out string plainText)
        {
            plainText = string.Empty;
            if (string.IsNullOrWhiteSpace(cipherText))
            {
                return false;
            }

            try
            {
                byte[] cipher = Convert.FromBase64String(cipherText);
                byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                plainText = Encoding.UTF8.GetString(plain);
                return !string.IsNullOrWhiteSpace(plainText);
            }
            catch
            {
                return false;
            }
        }
    }
}