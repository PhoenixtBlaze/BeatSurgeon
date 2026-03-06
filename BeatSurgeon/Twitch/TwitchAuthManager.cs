using System;
using System.Net;
using System.Net.Http;
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
    internal sealed class TwitchAuthManager : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TwitchAuthManager");
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private const string BackendBaseUrl = "https://phoenixblaze0.duckdns.org";
        private const string TwitchClientId = "dyq6orcrvl9cxd8d1usx6rtczt3tfb";

        private static TwitchAuthManager _instance;
        internal static TwitchAuthManager Instance => _instance ?? (_instance = new TwitchAuthManager());

        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        private string _accessToken;
        private string _refreshToken;
        private string _cachedChannelUserId;
        private Timer _refreshTimer;
        private CancellationTokenSource _loginCts;
        private volatile bool _loginInProgress;
        private volatile bool _authReadyRaised;

        public event Action OnTokensUpdated;
        public event Action OnIdentityUpdated;
        public event Action OnAuthReady;
        public event Action OnReauthRequired;

        internal string ClientId => TwitchClientId;
        internal string BroadcasterId { get; private set; }
        internal string BroadcasterLogin { get; private set; }
        internal string BotUserId { get; private set; }

        internal bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(_accessToken) &&
            ReadTokenExpiryUtc() > DateTime.UtcNow.AddMinutes(1);

        internal bool IsReauthRequired => PluginConfig.Instance?.TwitchReauthRequired == true;

        [Inject]
        public TwitchAuthManager()
        {
            _instance = this;
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize");

            LoadTokens();
            RestoreIdentityFromCache();

            if (PluginConfig.Instance.HasValidToken)
            {
                _log.Auth("ValidTokenFound - scheduling proactive refresh");
                ScheduleProactiveRefresh();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ValidateTokenAsync(CancellationToken.None).ConfigureAwait(false);
                        if (!IsReauthRequired)
                        {
                            await EnsureIdentityAsync(CancellationToken.None).ConfigureAwait(false);
                            RaiseAuthReadyIfPossible();
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex, "Initialize identity bootstrap");
                    }
                });
            }
            else
            {
                _log.Auth("NoValidToken - OAuth flow will be triggered on first Twitch operation");
            }
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
            if (!PluginConfig.Instance.HasValidToken || string.IsNullOrWhiteSpace(_accessToken))
            {
                _log.Auth("GetAccessToken - no valid token, refreshing");
                await RefreshTokenAsync(ct).ConfigureAwait(false);
            }

            return _accessToken ?? string.Empty;
        }

        internal string GetAccessToken() => _accessToken ?? string.Empty;

        internal async Task<string> GetChannelUserIdAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(_cachedChannelUserId))
            {
                return _cachedChannelUserId;
            }

            _log.Auth("GetChannelUserId - fetching from Twitch API");
            await EnsureIdentityAsync(ct).ConfigureAwait(false);
            _cachedChannelUserId = BroadcasterId ?? string.Empty;
            _log.Auth("GetChannelUserId", "userId=" + _cachedChannelUserId);
            return _cachedChannelUserId;
        }

        internal async Task<bool> EnsureReadyAsync(CancellationToken ct = default(CancellationToken))
        {
            await GetAccessTokenAsync(ct).ConfigureAwait(false);
            await EnsureIdentityAsync(ct).ConfigureAwait(false);
            RaiseAuthReadyIfPossible();

            return !string.IsNullOrWhiteSpace(_accessToken)
                && !string.IsNullOrWhiteSpace(BroadcasterId)
                && !string.IsNullOrWhiteSpace(BotUserId);
        }

        internal async Task EnsureValidTokenAsync(CancellationToken ct = default(CancellationToken))
        {
            if (PluginConfig.Instance.HasValidToken && !string.IsNullOrWhiteSpace(_accessToken))
            {
                return;
            }

            await RefreshTokenAsync(ct).ConfigureAwait(false);
        }

        internal async Task InitiateLogin()
        {
            if (_loginInProgress)
            {
                PluginConfig.Instance.BackendStatus = "Login already in progress";
                return;
            }

            _loginInProgress = true;
            _loginCts?.Cancel();
            _loginCts = new CancellationTokenSource();

            try
            {
                string state = Guid.NewGuid().ToString("N");
                PluginConfig.Instance.BackendStatus = "Opening browser...";
                string loginUrl = BackendBaseUrl + "/login?state=" + Uri.EscapeDataString(state);
                Application.OpenURL(loginUrl);

                PluginConfig.Instance.BackendStatus = "Waiting for authorization...";
                await PollForBackendTokenAsync(state, _loginCts.Token).ConfigureAwait(false);
                await EnsureIdentityAsync(_loginCts.Token).ConfigureAwait(false);
                ScheduleProactiveRefresh();
                RaiseAuthReadyIfPossible();

                PluginConfig.Instance.BackendStatus = "Connected";
                _log.Auth("LoginSucceeded");
            }
            catch (OperationCanceledException)
            {
                PluginConfig.Instance.BackendStatus = "Login cancelled";
                _log.Auth("LoginCancelled");
            }
            catch (Exception ex)
            {
                PluginConfig.Instance.BackendStatus = "Login failed";
                _log.Exception(ex, "InitiateLogin");
            }
            finally
            {
                _loginInProgress = false;
            }
        }

        internal void CancelLogin()
        {
            try
            {
                _loginCts?.Cancel();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "CancelLogin");
            }
        }

        internal void Logout()
        {
            _accessToken = string.Empty;
            _refreshToken = string.Empty;
            _cachedChannelUserId = string.Empty;
            BroadcasterId = string.Empty;
            BroadcasterLogin = string.Empty;
            BotUserId = string.Empty;
            _authReadyRaised = false;

            PersistTokenState();
            PluginConfig.Instance.ChannelUserId = string.Empty;
            PluginConfig.Instance.CachedBroadcasterId = string.Empty;
            PluginConfig.Instance.CachedBroadcasterLogin = string.Empty;
            PluginConfig.Instance.CachedBotUserId = string.Empty;
            PluginConfig.Instance.CachedBotUserLogin = string.Empty;
            PluginConfig.Instance.BackendStatus = "Not connected";
            ClearTwitchReauthRequired();

            OnTokensUpdated?.Invoke();
            OnIdentityUpdated?.Invoke();
        }

        private async Task PollForBackendTokenAsync(string state, CancellationToken ct)
        {
            int attempts = 0;
            const int maxAttempts = 90;

            while (attempts++ < maxAttempts)
            {
                ct.ThrowIfCancellationRequested();

                string url = BackendBaseUrl + "/token?state=" + Uri.EscapeDataString(state);
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
                        throw new InvalidOperationException("Backend token response did not contain access/refresh tokens.");
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

                    ClearTwitchReauthRequired();
                    OnTokensUpdated?.Invoke();
                    return;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    continue;
                }

                throw new HttpRequestException("Backend /token failed: " + response.StatusCode + " body=" + body);
            }

            throw new TimeoutException("Timed out waiting for backend OAuth token handoff.");
        }

        private async Task EnsureIdentityAsync(CancellationToken ct = default(CancellationToken))
        {
            await EnsureValidTokenAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(BroadcasterId) && !string.IsNullOrWhiteSpace(BotUserId))
            {
                return;
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users"))
            {
                request.Headers.TryAddWithoutValidation("Client-Id", TwitchClientId);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _accessToken);

                using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException("Twitch /users failed: " + response.StatusCode + " body=" + body);
                    }

                    JObject json = JObject.Parse(body);
                    JToken user = json["data"]?[0];
                    if (user == null)
                    {
                        throw new InvalidOperationException("Twitch /users returned no user payload.");
                    }

                    BroadcasterId = user["id"]?.ToString() ?? string.Empty;
                    BroadcasterLogin = user["login"]?.ToString() ?? string.Empty;
                    BotUserId = BroadcasterId;
                    _cachedChannelUserId = BroadcasterId;

                    PluginConfig.Instance.ChannelUserId = BroadcasterId;
                    PluginConfig.Instance.CachedBroadcasterId = BroadcasterId;
                    PluginConfig.Instance.CachedBroadcasterLogin = BroadcasterLogin;
                    PluginConfig.Instance.CachedBotUserId = BotUserId;
                    PluginConfig.Instance.CachedBotUserLogin = BroadcasterLogin;
                    OnIdentityUpdated?.Invoke();
                }
            }
        }

        private async Task RefreshTokenAsync(CancellationToken ct)
        {
            _log.Auth("RefreshStarted");

            if (string.IsNullOrWhiteSpace(_refreshToken))
            {
                _log.Auth("RefreshSkipped", "No refresh token available");
                return;
            }

            await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string url = BackendBaseUrl + "/refresh";
                string jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(new { refresh_token = _refreshToken });
                using (var refreshRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new System.Net.Http.StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
                })
                using (HttpResponseMessage response = await _http.SendAsync(refreshRequest, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _log.Auth("RefreshFailed", "status=" + response.StatusCode);
                        throw new HttpRequestException("Refresh failed: " + response.StatusCode + " body=" + body);
                    }

                    JObject json = JObject.Parse(body);
                    string access = json["access_token"]?.ToString();
                    string refresh = json["refresh_token"]?.ToString();
                    int expiresIn = json["expires_in"]?.Value<int>() ?? 3600;

                    if (string.IsNullOrWhiteSpace(access))
                    {
                        throw new InvalidOperationException("Refresh response missing access_token.");
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
                ClearTwitchReauthRequired();
                OnTokensUpdated?.Invoke();
                ScheduleProactiveRefresh();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RefreshTokenAsync");
                _log.Auth("RefreshFailed - Twitch commands will fail until user re-authenticates");
                SetTwitchReauthRequired("Token refresh failed: " + ex.GetType().Name);
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
                        SetTwitchReauthRequired("Immediate token refresh failed");
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
                    SetTwitchReauthRequired("Proactive token refresh failed");
                }
            }, null, refreshAt, Timeout.InfiniteTimeSpan);
        }

        private void RaiseAuthReadyIfPossible()
        {
            if (_authReadyRaised) return;
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(BroadcasterId)) return;

            _authReadyRaised = true;
            _log.Auth("AuthReady", "broadcasterId=" + BroadcasterId);
            OnAuthReady?.Invoke();
        }

        private void SetTwitchReauthRequired(string reason)
        {
            _log.Auth("SetTwitchReauthRequired", reason);
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg != null)
            {
                cfg.TwitchReauthRequired = true;
            }
            _authReadyRaised = false;
            OnReauthRequired?.Invoke();
        }

        internal void ClearTwitchReauthRequired()
        {
            _log.Auth("ClearTwitchReauthRequired");
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg != null)
            {
                cfg.TwitchReauthRequired = false;
            }
        }

        internal void MarkReauthRequired(string reason)
        {
            if (IsReauthRequired)
            {
                return;
            }

            SetTwitchReauthRequired(reason);
        }

        /// <summary>
        /// Forces a token refresh regardless of local expiry state.
        /// Called by TwitchApiClient when Helix returns a 401 Unauthorized.
        /// </summary>
        internal async Task ForceRefreshTokenAsync(CancellationToken ct = default(CancellationToken))
        {
            try
            {
                await RefreshTokenAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "ForceRefreshTokenAsync");
                MarkReauthRequired("Forced refresh failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// Validates the stored access token against Twitch's /oauth2/validate endpoint on startup.
        /// Catches tokens that are locally valid (expiry not exceeded) but server-side revoked.
        /// On 401, attempts a proactive refresh; if that fails, marks reauth required.
        /// </summary>
        private async Task ValidateTokenAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_accessToken)) return;

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate"))
                {
                    // Token validation uses "OAuth" not "Bearer" per Twitch docs.
                    request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + _accessToken);

                    using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            _log.Auth("StartupTokenValidation", "Token rejected by Twitch (401) - forcing refresh");
                            try
                            {
                                await RefreshTokenAsync(ct).ConfigureAwait(false);
                            }
                            catch (Exception refreshEx)
                            {
                                _log.Exception(refreshEx, "ValidateTokenAsync token refresh on 401");
                                SetTwitchReauthRequired("Startup token validation failed - please re-authenticate");
                            }
                            return;
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            JObject json = JObject.Parse(body);
                            string login = json["login"]?.ToString() ?? "(unknown)";
                            int scopeCount = (json["scopes"] as JArray)?.Count ?? 0;
                            _log.Auth("StartupTokenValidation", "OK login=" + login + " scopes=" + scopeCount);
                        }
                        else
                        {
                            _log.Warn("StartupTokenValidation - unexpected status=" + (int)response.StatusCode + " (non-fatal, continuing)");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Network error during validation is non-fatal - we proceed with the cached token.
                _log.Exception(ex, "ValidateTokenAsync - skipping validation, proceeding with cached token");
            }
        }

        private void LoadTokens()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            _accessToken = cfg.AccessToken ?? string.Empty;
            _refreshToken = cfg.RefreshToken ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_accessToken) && !string.IsNullOrWhiteSpace(cfg.EncryptedAccessToken))
            {
                _accessToken = DecryptString(cfg.EncryptedAccessToken);
            }

            if (string.IsNullOrWhiteSpace(_refreshToken) && !string.IsNullOrWhiteSpace(cfg.EncryptedRefreshToken))
            {
                _refreshToken = DecryptString(cfg.EncryptedRefreshToken);
            }

            if (cfg.TokenExpiry == DateTime.MinValue && cfg.TokenExpiryTicks > 0)
            {
                cfg.TokenExpiry = new DateTime(cfg.TokenExpiryTicks, DateTimeKind.Utc);
            }
        }

        private void PersistTokenState()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            cfg.AccessToken = _accessToken ?? string.Empty;
            cfg.RefreshToken = _refreshToken ?? string.Empty;

            // Maintain backwards compatibility with prior encrypted config fields.
            cfg.EncryptedAccessToken = EncryptString(cfg.AccessToken);
            cfg.EncryptedRefreshToken = EncryptString(cfg.RefreshToken);
            cfg.TokenExpiryTicks = cfg.TokenExpiry.ToUniversalTime().Ticks;
        }

        private void RestoreIdentityFromCache()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            BroadcasterId = string.IsNullOrWhiteSpace(cfg.ChannelUserId) ? cfg.CachedBroadcasterId : cfg.ChannelUserId;
            BroadcasterLogin = cfg.CachedBroadcasterLogin ?? string.Empty;
            BotUserId = string.IsNullOrWhiteSpace(cfg.CachedBotUserId) ? BroadcasterId : cfg.CachedBotUserId;
            _cachedChannelUserId = BroadcasterId ?? string.Empty;
        }

        private DateTime ReadTokenExpiryUtc()
        {
            DateTime expiry = PluginConfig.Instance.TokenExpiry;
            if (expiry == DateTime.MinValue && PluginConfig.Instance.TokenExpiryTicks > 0)
            {
                expiry = new DateTime(PluginConfig.Instance.TokenExpiryTicks, DateTimeKind.Utc);
                PluginConfig.Instance.TokenExpiry = expiry;
            }

            if (expiry.Kind == DateTimeKind.Unspecified)
            {
                expiry = DateTime.SpecifyKind(expiry, DateTimeKind.Utc);
            }

            return expiry.ToUniversalTime();
        }

        private void WriteTokenExpiryUtc(DateTime expiry)
        {
            PluginConfig.Instance.TokenExpiry = expiry.ToUniversalTime();
            PluginConfig.Instance.TokenExpiryTicks = PluginConfig.Instance.TokenExpiry.Ticks;
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

        private string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
            {
                return string.Empty;
            }

            try
            {
                byte[] cipher = Convert.FromBase64String(cipherText);
                byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
