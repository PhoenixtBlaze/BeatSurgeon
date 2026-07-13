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

namespace BeatSurgeon.YouTube
{
    internal sealed class YouTubeAuthManager : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("YouTubeAuthManager");
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private const string BackendBaseUrl = "https://phoenixblaze0.duckdns.org";
        private const string YouTubeChannelsUrl =
            "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true";

        private static YouTubeAuthManager _instance;
        internal static YouTubeAuthManager Instance => _instance ?? (_instance = new YouTubeAuthManager());

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

        internal string ChannelId { get; private set; }
        internal string ChannelTitle { get; private set; }

        internal bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(_accessToken) &&
            ReadTokenExpiryUtc() > DateTime.UtcNow.AddMinutes(1);

        internal bool IsReauthRequired => PluginConfig.Instance?.YouTubeReauthRequired == true;

        [Inject]
        public YouTubeAuthManager()
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
                        if (PluginConfig.Instance.HasValidYouTubeToken)
                        {
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(PluginConfig.Instance?.YouTubeEncryptedAccessToken))
                        {
                            break;
                        }

                        _log.Auth($"Config not ready yet (attempt {attempt + 1}/5) - retrying in 200 ms...");
                        await Task.Delay(200).ConfigureAwait(false);
                        LoadTokens();
                        RestoreIdentityFromCache();
                    }

                    if (!PluginConfig.Instance.HasValidYouTubeToken && !string.IsNullOrWhiteSpace(_refreshToken))
                    {
                        _log.Auth("StartupRefresh", "YouTube access token missing or expired - refreshing from saved refresh token");
                        try
                        {
                            await RefreshTokenAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _log.Exception(ex, "StartupRefresh");
                        }
                    }

                    if (PluginConfig.Instance.HasValidYouTubeToken)
                    {
                        _log.Auth("ValidTokenFound - scheduling proactive refresh");
                        ScheduleProactiveRefresh();
                        if (!IsReauthRequired)
                        {
                            await BootstrapIdentityAsync(CancellationToken.None).ConfigureAwait(false);
                            RaiseAuthReadyIfPossible();
                        }
                    }
                    else
                    {
                        _log.Auth("NoValidToken - YouTube OAuth will run when user connects from settings");
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
            _log.Lifecycle("Dispose - stopping token refresh timer");
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
            if (!PluginConfig.Instance.HasValidYouTubeToken || string.IsNullOrWhiteSpace(_accessToken))
            {
                _log.Auth("GetAccessToken - no valid token, refreshing");
                await RefreshTokenAsync(ct).ConfigureAwait(false);
            }

            return _accessToken ?? string.Empty;
        }

        internal async Task<bool> EnsureReadyAsync(CancellationToken ct = default(CancellationToken))
        {
            await GetAccessTokenAsync(ct).ConfigureAwait(false);
            await BootstrapIdentityAsync(ct).ConfigureAwait(false);
            RaiseAuthReadyIfPossible();

            return !string.IsNullOrWhiteSpace(_accessToken)
                && !string.IsNullOrWhiteSpace(ChannelId);
        }

        internal async Task InitiateLogin()
        {
            if (_loginInProgress)
            {
                PluginConfig.Instance.YouTubeBackendStatus = "Login already in progress";
                return;
            }

            _loginInProgress = true;
            _loginCts?.Cancel();
            _loginCts = new CancellationTokenSource();

            try
            {
                string state = Guid.NewGuid().ToString("N");
                PluginConfig.Instance.YouTubeBackendStatus = "Opening browser...";
                string loginUrl = BackendBaseUrl + "/youtube/login?state=" + Uri.EscapeDataString(state);
                Application.OpenURL(loginUrl);

                PluginConfig.Instance.YouTubeBackendStatus = "Waiting for authorization...";
                await PollForBackendTokenAsync(state, _loginCts.Token).ConfigureAwait(false);
                await BootstrapIdentityAsync(_loginCts.Token).ConfigureAwait(false);
                ScheduleProactiveRefresh();
                RaiseAuthReadyIfPossible();

                PluginConfig.Instance.YouTubeBackendStatus = "Connected";
                _log.Auth("LoginSucceeded");
            }
            catch (OperationCanceledException)
            {
                PluginConfig.Instance.YouTubeBackendStatus = "Login cancelled";
                _log.Auth("LoginCancelled");
            }
            catch (Exception ex)
            {
                PluginConfig.Instance.YouTubeBackendStatus = "Login failed";
                _log.Exception(ex, "InitiateLogin");
            }
            finally
            {
                _loginInProgress = false;
            }
        }

        internal void Logout()
        {
            _log.Auth("Logout", "Clearing saved YouTube credentials");
            _refreshTimer?.Dispose();
            _refreshTimer = null;

            _accessToken = string.Empty;
            _refreshToken = string.Empty;
            ChannelId = string.Empty;
            ChannelTitle = string.Empty;
            _authReadyRaised = false;

            PersistTokenState();
            PluginConfig.Instance.CachedYouTubeChannelId = string.Empty;
            PluginConfig.Instance.CachedYouTubeChannelTitle = string.Empty;
            PluginConfig.Instance.YouTubeBackendStatus = "Not connected";
            ClearYouTubeReauthRequired();

            OnTokensUpdated?.Invoke();
            OnIdentityUpdated?.Invoke();
        }

        internal void ClearYouTubeReauthRequired()
        {
            _log.Auth("ClearYouTubeReauthRequired");
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg != null)
            {
                cfg.YouTubeReauthRequired = false;
            }
        }

        private void SetYouTubeReauthRequired(string reason)
        {
            _log.Auth("SetYouTubeReauthRequired", reason);
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg != null)
            {
                cfg.YouTubeReauthRequired = true;
            }

            _authReadyRaised = false;
            OnReauthRequired?.Invoke();
        }

        private async Task PollForBackendTokenAsync(string state, CancellationToken ct)
        {
            int attempts = 0;
            const int maxAttempts = 90;

            while (attempts++ < maxAttempts)
            {
                ct.ThrowIfCancellationRequested();

                string url = BackendBaseUrl + "/youtube/token?state=" + Uri.EscapeDataString(state);
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

                    if (string.IsNullOrWhiteSpace(access))
                    {
                        throw new InvalidOperationException("Backend YouTube token response did not contain an access token.");
                    }

                    await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        _accessToken = access;
                        if (!string.IsNullOrWhiteSpace(refresh))
                        {
                            _refreshToken = refresh;
                        }

                        WriteTokenExpiryUtc(DateTime.UtcNow.AddSeconds(expiresIn));
                        PersistTokenState();
                    }
                    finally
                    {
                        _tokenLock.Release();
                    }

                    ClearYouTubeReauthRequired();
                    OnTokensUpdated?.Invoke();
                    return;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    continue;
                }

                throw new HttpRequestException("Backend /youtube/token failed: " + response.StatusCode + " body=" + body);
            }

            throw new TimeoutException("Timed out waiting for YouTube OAuth token handoff.");
        }

        private async Task BootstrapIdentityAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                return;
            }

            await EnsureIdentityAsync(ct).ConfigureAwait(false);
        }

        private async Task EnsureIdentityAsync(CancellationToken ct = default(CancellationToken))
        {
            await EnsureValidTokenAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(ChannelId))
            {
                return;
            }

            if (await TryFetchIdentityAsync(_accessToken, ct).ConfigureAwait(false))
            {
                return;
            }

            _log.Auth("EnsureIdentity", "YouTube identity rejected token - refreshing");
            await RefreshTokenAsync(ct).ConfigureAwait(false);

            if (await TryFetchIdentityAsync(_accessToken, ct).ConfigureAwait(false))
            {
                return;
            }

            throw new HttpRequestException("YouTube identity fetch failed after token refresh.");
        }

        private async Task<bool> TryFetchIdentityAsync(string accessToken, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, YouTubeChannelsUrl))
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
                        throw new HttpRequestException("YouTube channels fetch failed: " + response.StatusCode + " body=" + body);
                    }

                    JObject json = JObject.Parse(body);
                    JToken channel = json["items"]?[0];
                    if (channel == null)
                    {
                        throw new InvalidOperationException("YouTube channels API returned no channel for this account.");
                    }

                    ChannelId = channel["id"]?.ToString() ?? string.Empty;
                    ChannelTitle = channel["snippet"]?["title"]?.ToString() ?? string.Empty;

                    PluginConfig.Instance.CachedYouTubeChannelId = ChannelId;
                    PluginConfig.Instance.CachedYouTubeChannelTitle = ChannelTitle;
                    OnIdentityUpdated?.Invoke();
                    return true;
                }
            }
        }

        private async Task EnsureValidTokenAsync(CancellationToken ct = default(CancellationToken))
        {
            if (PluginConfig.Instance.HasValidYouTubeToken && !string.IsNullOrWhiteSpace(_accessToken))
            {
                return;
            }

            await RefreshTokenAsync(ct).ConfigureAwait(false);
        }

        private async Task RefreshTokenAsync(CancellationToken ct)
        {
            _log.Auth("RefreshStarted");

            if (string.IsNullOrWhiteSpace(_refreshToken))
            {
                _log.Auth("RefreshSkipped", "No YouTube refresh token available");
                return;
            }

            await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string url = BackendBaseUrl + "/youtube/refresh";
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
                        throw new HttpRequestException("YouTube refresh failed: " + response.StatusCode + " body=" + body);
                    }

                    JObject json = JObject.Parse(body);
                    string access = json["access_token"]?.ToString();
                    string refresh = json["refresh_token"]?.ToString();
                    int expiresIn = json["expires_in"]?.Value<int>() ?? 3600;

                    if (string.IsNullOrWhiteSpace(access))
                    {
                        throw new InvalidOperationException("YouTube refresh response missing access_token.");
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
                ClearYouTubeReauthRequired();
                OnTokensUpdated?.Invoke();
                ScheduleProactiveRefresh();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RefreshTokenAsync");
                _log.Auth("RefreshFailed - YouTube features will fail until user re-authenticates");
                SetYouTubeReauthRequired("YouTube token refresh failed: " + ex.GetType().Name);
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
                        SetYouTubeReauthRequired("Immediate YouTube token refresh failed");
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
                    SetYouTubeReauthRequired("Proactive YouTube token refresh failed");
                }
            }, null, refreshAt, Timeout.InfiniteTimeSpan);
        }

        private void RaiseAuthReadyIfPossible()
        {
            if (_authReadyRaised)
            {
                return;
            }

            if (!IsAuthenticated || string.IsNullOrWhiteSpace(ChannelId))
            {
                return;
            }

            _authReadyRaised = true;
            _log.Auth("AuthReady", "channelId=" + ChannelId);
            OnAuthReady?.Invoke();
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
                cfg.YouTubeAccessToken,
                cfg.YouTubeEncryptedAccessToken,
                out normalizeStoredAccessToken);
            _refreshToken = ReadStoredToken(
                cfg.YouTubeRefreshToken,
                cfg.YouTubeEncryptedRefreshToken,
                out normalizeStoredRefreshToken);

            if (normalizeStoredAccessToken || normalizeStoredRefreshToken)
            {
                _log.Auth("LoadTokens", "Normalizing stored YouTube tokens to encrypted-only config fields");
                PersistTokenState();
            }

            if (cfg.YouTubeTokenExpiry == DateTime.MinValue && cfg.YouTubeTokenExpiryTicks > 0)
            {
                cfg.YouTubeTokenExpiry = new DateTime(cfg.YouTubeTokenExpiryTicks, DateTimeKind.Utc);
            }
        }

        private void PersistTokenState()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            cfg.YouTubeAccessToken = string.Empty;
            cfg.YouTubeRefreshToken = string.Empty;
            cfg.YouTubeEncryptedAccessToken = EncryptString(_accessToken ?? string.Empty);
            cfg.YouTubeEncryptedRefreshToken = EncryptString(_refreshToken ?? string.Empty);
            cfg.YouTubeTokenExpiryTicks = cfg.YouTubeTokenExpiry.ToUniversalTime().Ticks;
            cfg.Changed();
        }

        private void RestoreIdentityFromCache()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            ChannelId = cfg.CachedYouTubeChannelId ?? string.Empty;
            ChannelTitle = cfg.CachedYouTubeChannelTitle ?? string.Empty;
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

        private DateTime ReadTokenExpiryUtc()
        {
            DateTime expiry = PluginConfig.Instance.YouTubeTokenExpiry;
            if (expiry == DateTime.MinValue && PluginConfig.Instance.YouTubeTokenExpiryTicks > 0)
            {
                expiry = new DateTime(PluginConfig.Instance.YouTubeTokenExpiryTicks, DateTimeKind.Utc);
                PluginConfig.Instance.YouTubeTokenExpiry = expiry;
            }

            if (expiry.Kind == DateTimeKind.Unspecified)
            {
                expiry = DateTime.SpecifyKind(expiry, DateTimeKind.Utc);
            }

            return expiry.ToUniversalTime();
        }

        private void WriteTokenExpiryUtc(DateTime expiry)
        {
            PluginConfig.Instance.YouTubeTokenExpiry = expiry.ToUniversalTime();
            PluginConfig.Instance.YouTubeTokenExpiryTicks = PluginConfig.Instance.YouTubeTokenExpiry.Ticks;
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
