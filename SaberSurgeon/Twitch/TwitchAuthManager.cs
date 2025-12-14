using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SaberSurgeon.Twitch
{
    /// <summary>
    /// Twitch auth + identity resolver for the new architecture:
    /// - Backend is ONLY used for browser login + one-time token handoff (/login + /token).
    /// - The mod uses the returned user access token for Helix (supporter check, eventsub subscription creation).
    /// - EventSub is handled in-game by TwitchEventSubClient (WebSocket).
    /// </summary>
    public class TwitchAuthManager
    {
        // Your backend base URL (no trailing slash)
        private const string BackendBaseUrl = "https://phoenixblaze0.duckdns.org";

        // Twitch application client ID (used for refresh + Helix headers)
        private const string TwitchClientId = "dyq6orcrvl9cxd8d1usx6rtczt3tfb";
        public string ClientId => TwitchClientId;

        public const string SupportChannelName = "phoenixblaze0";

        private static TwitchAuthManager _instance;
        public static TwitchAuthManager Instance => _instance ?? (_instance = new TwitchAuthManager());

        // Tokens (encrypted at rest in Plugin.Settings)
        private string _accessToken;
        private string _refreshToken;

        // Identity
        public string BroadcasterId { get; private set; }   // logged-in user id
        public string BroadcasterLogin { get; private set; } // logged-in login
        public string BotUserId { get; private set; }       // for now: same as broadcaster (can be separated later)

        // Events for the rest of your mod to hook into
        public event Action OnTokensUpdated;
        public event Action OnIdentityUpdated;

        // Prevent concurrent refresh / identity calls
        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        // Per-machine encryption for stored tokens
        private readonly byte[] _entropy =
            Encoding.UTF8.GetBytes(SystemInfo.deviceUniqueIdentifier.Substring(0, 16));

        // Reuse one HttpClient (important: avoid socket exhaustion)
        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// True if there is a non-empty access token and its cached expiry has not passed.
        /// </summary>
        public bool IsAuthenticated =>
            !string.IsNullOrEmpty(_accessToken) &&
            DateTime.UtcNow.Ticks < Plugin.Settings.TokenExpiryTicks;



        private bool HasRefreshToken => !string.IsNullOrEmpty(_refreshToken);


        /// <summary>
        /// Used by other components (Helix/EventSub) to get the current access token.
        /// Prefer calling GetValidAccessTokenAsync() if you're about to do network work.
        /// </summary>
        public string GetAccessToken() => _accessToken;

        /// <summary>
        /// Load tokens from config. If valid, refresh (if needed) and resolve identity.
        /// Call once from Plugin.Init / OnEnable.
        /// </summary>
        public void Initialize()
        {
            LoadTokens();
            RestoreIdentityFromCache();

            if (!HasRefreshToken && !IsAuthenticated)
            {
                Plugin.Settings.BackendStatus = "Not connected";
                return;
            }

            Plugin.Log.Info("TwitchAuth: Found cached tokens. Auto-connecting...");
            Plugin.Settings.BackendStatus = "Connected";

            // Fire and forget: refresh/identity/supporter info in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsureValidTokenAsync();
                    await EnsureIdentityAsync();

                    
                    // Optional: refresh supporter tier cache after identity is known
                    await TwitchApiClient.Instance.FetchBroadcasterAndSupportInfo();
                    RestoreIdentityFromCache();

                    OnTokensUpdated?.Invoke();
                    OnIdentityUpdated?.Invoke();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error("TwitchAuth: Initialize background task failed: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Starts browser-based login via backend /login, then polls /token until it returns tokens.
        /// After token retrieval: resolves user identity via Helix /users.
        /// </summary>
        public async Task InitiateLogin()
        {
            try
            {
                string state = Guid.NewGuid().ToString("N");
                Plugin.Log.Info($"TwitchAuth: Opening backend login with state={state}");
                Plugin.Settings.BackendStatus = "Opening browser...";

                string loginUrl = $"{BackendBaseUrl}/login?state={Uri.EscapeDataString(state)}";
                Application.OpenURL(loginUrl);

                await PollForBackendToken(state);

                // Token received — now resolve identity and supporter tier
                await EnsureIdentityAsync();
                await TwitchApiClient.Instance.FetchBroadcasterAndSupportInfo();

                Plugin.Settings.BackendStatus = "Connected";
                OnTokensUpdated?.Invoke();
                OnIdentityUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("TwitchAuth: InitiateLogin failed: " + ex.Message);
                Plugin.Settings.BackendStatus = "Login failed";
            }
        }

        /// <summary>
        /// Poll backend /token?state=... until it returns JSON with access_token/refresh_token.
        /// Backend should also return expires_in when possible (your new server.js does).
        /// </summary>
        private async Task PollForBackendToken(string state)
        {
            if (string.IsNullOrEmpty(state))
                return;

            Plugin.Settings.BackendStatus = "Waiting for authorization...";

            try
            {
                int attempts = 0; // up to ~3 minutes
                while (attempts < 90)
                {
                    attempts++;

                    string url = $"{BackendBaseUrl}/token?state={Uri.EscapeDataString(state)}";
                    HttpResponseMessage resp = await _http.GetAsync(url);
                    string jsonText = await resp.Content.ReadAsStringAsync();

                    if (resp.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(jsonText);

                        string access = json["access_token"]?.ToString();
                        string refresh = json["refresh_token"]?.ToString();
                        int expiresIn = json["expires_in"]?.Value<int?>() ?? 0;

                        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
                        {
                            Plugin.Log.Error("TwitchAuth: Backend /token missing access_token or refresh_token");
                            Plugin.Settings.BackendStatus = "Token error";
                            return;
                        }

                        await _tokenLock.WaitAsync();
                        try
                        {
                            _accessToken = access;
                            _refreshToken = refresh;

                            // If backend gave expires_in, use it; otherwise fall back to 4 hours.
                            if (expiresIn > 0)
                                Plugin.Settings.TokenExpiryTicks = DateTime.UtcNow.AddSeconds(expiresIn).Ticks;
                            else
                                Plugin.Settings.TokenExpiryTicks = DateTime.UtcNow.AddHours(4).Ticks;

                            SaveTokens();
                        }
                        finally
                        {
                            _tokenLock.Release();
                        }

                        Plugin.Log.Info("TwitchAuth: Tokens received, IsAuthenticated=" + IsAuthenticated);
                        Plugin.Settings.BackendStatus = "Connected";
                        return;
                    }

                    // 404 means pending
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    Plugin.Log.Error($"TwitchAuth: Backend /token HTTP {(int)resp.StatusCode}: {jsonText}");
                    Plugin.Settings.BackendStatus = "Token error";
                    return;
                }

                Plugin.Log.Warn("TwitchAuth: /token polling timed out");
                Plugin.Settings.BackendStatus = "Login timeout";
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("TwitchAuth: PollForBackendToken exception: " + ex.Message);
                Plugin.Settings.BackendStatus = "Login error";
            }
        }

        /// <summary>
        /// Ensures we have a valid token (refresh if within 5 minutes of expiry).
        /// Safe to call often.
        /// </summary>
        public async Task EnsureValidTokenAsync()
        {
            await _tokenLock.WaitAsync();
            try
            {
                // Only refresh when within 5 minutes of expiry
                if (DateTime.UtcNow.AddMinutes(5).Ticks <= Plugin.Settings.TokenExpiryTicks)
                    return;

                if (string.IsNullOrEmpty(_refreshToken))
                    return;

                Plugin.Log.Info("TwitchAuth: Refreshing token...");

                string url = $"{BackendBaseUrl}/refresh?refresh_token={Uri.EscapeDataString(_refreshToken)}";
                HttpResponseMessage response = await _http.GetAsync(url);
                string responseString = await response.Content.ReadAsStringAsync();
                Plugin.Log.Info("TwitchAuth: Refresh HTTP=" + (int)response.StatusCode);

                if (response.IsSuccessStatusCode)
                {
                    ParseAndSaveTokens(responseString);
                    Plugin.Log.Info("TwitchAuth: Refresh successful");
                    Plugin.Settings.BackendStatus = "Connected";
                }
                else
                {
                    Plugin.Log.Error("TwitchAuth: Refresh failed: " + responseString);
                    Logout();
                    Plugin.Settings.BackendStatus = "Not connected (login required)";
                }
                
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("TwitchAuth: EnsureValidTokenAsync exception: " + ex.Message);
                Plugin.Settings.BackendStatus = "Refresh error";
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        /// <summary>
        /// Resolve the logged-in user's identity via Helix /users.
        /// This is what you should use to populate BroadcasterId/BotUserId for EventSub.
        /// </summary>
        public async Task EnsureIdentityAsync()
        {
            await EnsureValidTokenAsync();

            if (string.IsNullOrEmpty(_accessToken))
                return;

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Client-Id", ClientId);
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _accessToken);

                    var userRes = await client.GetAsync("https://api.twitch.tv/helix/users");
                    if (!userRes.IsSuccessStatusCode)
                    {
                        string err = await userRes.Content.ReadAsStringAsync();
                        Plugin.Log.Warn($"TwitchAuth: /users failed status={userRes.StatusCode} body={err}");
                        return;
                    }

                    var text = await userRes.Content.ReadAsStringAsync();
                    var json = JObject.Parse(text);
                    var data = json["data"]?[0];
                    if (data == null)
                    {
                        Plugin.Log.Warn("TwitchAuth: /users returned no data.");
                        return;
                    }

                    BroadcasterId = data["id"]?.ToString();
                    BroadcasterLogin = data["login"]?.ToString();

                    // Minimal setup: use the same account as the "bot" account for EventSub conditions.
                    BotUserId = BroadcasterId;

                    // Store caches if your PluginConfig supports these fields
                    Plugin.Settings.CachedBroadcasterId = BroadcasterId;
                    Plugin.Settings.CachedBroadcasterLogin = BroadcasterLogin;

                    Plugin.Log.Info($"TwitchAuth: Identity resolved. id={BroadcasterId}, login={BroadcasterLogin}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("TwitchAuth: EnsureIdentityAsync exception: " + ex.Message);
            }
        }

        /// <summary>
        /// For manual wiring (e.g., if you want a separate bot user id later).
        /// </summary>
        public void SetIds(string broadcasterId, string botUserId)
        {
            BroadcasterId = broadcasterId;
            BotUserId = botUserId;
        }

        public void Logout()
        {
            _accessToken = null;
            _refreshToken = null;

            BroadcasterId = null;
            BroadcasterLogin = null;
            BotUserId = null;

            Plugin.Settings.EncryptedAccessToken = string.Empty;
            Plugin.Settings.EncryptedRefreshToken = string.Empty;
            Plugin.Settings.TokenExpiryTicks = 0;

            Plugin.Settings.BackendStatus = "Not connected";

            OnTokensUpdated?.Invoke();
            OnIdentityUpdated?.Invoke();
        }

        private void ParseAndSaveTokens(string jsonResponse)
        {
            var json = JObject.Parse(jsonResponse);

            _accessToken = json["access_token"]?.ToString();
            _refreshToken = json["refresh_token"]?.ToString();

            int expiresIn = json["expires_in"]?.Value<int?>() ?? 3600;
            Plugin.Settings.TokenExpiryTicks = DateTime.UtcNow.AddSeconds(expiresIn).Ticks;

            SaveTokens();
        }

        private void SaveTokens()
        {
            Plugin.Settings.EncryptedAccessToken = EncryptString(_accessToken);
            Plugin.Settings.EncryptedRefreshToken = EncryptString(_refreshToken);
        }

        private void LoadTokens()
        {
            _accessToken = DecryptString(Plugin.Settings.EncryptedAccessToken);
            _refreshToken = DecryptString(Plugin.Settings.EncryptedRefreshToken);
        }

        private string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, _entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cipherBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RestoreIdentityFromCache()
        {
            BroadcasterId = Plugin.Settings.CachedBroadcasterId;

            // For now: bot == broadcaster unless you later add a separate bot login flow
            BotUserId = !string.IsNullOrEmpty(Plugin.Settings.CachedBotUserId)
                ? Plugin.Settings.CachedBotUserId
                : BroadcasterId;
        }

        public async Task<bool> EnsureReadyAsync()
        {
            await EnsureValidTokenAsync();
            await EnsureIdentityAsync();
            return !string.IsNullOrEmpty(_accessToken)
                && !string.IsNullOrEmpty(BroadcasterId)
                && !string.IsNullOrEmpty(BotUserId);
        }


    }
}
