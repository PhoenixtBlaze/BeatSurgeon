using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BeatSurgeon.Twitch
{
    public sealed class TwitchApiClient
    {
        public static event Action OnSubscriberStatusChanged;

        private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private static readonly HttpClient _http = new HttpClient();

        private static TwitchApiClient _instance;
        public static TwitchApiClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TwitchApiClient();
                }
                return _instance;
            }
        }

        public string BroadcasterId { get; private set; }
        public string BroadcasterName { get; private set; }

        private const string HelixUrl = "https://api.twitch.tv/helix";
        private const string BackendEntitlementsUrl = "https://phoenixblaze0.duckdns.org/entitlements";
        private const string BackendBaseUrl = "https://phoenixblaze0.duckdns.org";

        private TwitchApiClient() { }

        public static IEnumerator GetSpriteFromUrl(string url, Action<Sprite> callback)
        {
            if (string.IsNullOrEmpty(url))
            {
                callback?.Invoke(null);
                yield break;
            }

            if (_spriteCache.TryGetValue(url, out var cached) && cached != null)
            {
                callback?.Invoke(cached);
                yield break;
            }

            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(null);
                    yield break;
                }

                var texture = DownloadHandlerTexture.GetContent(req);
                if (texture == null)
                {
                    callback?.Invoke(null);
                    yield break;
                }

                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                _spriteCache[url] = sprite;
                callback?.Invoke(sprite);
            }
        }

        public static void ClearCache()
        {
            foreach (var sprite in _spriteCache.Values)
            {
                if (sprite == null) continue;
                if (sprite.texture != null) UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
            _spriteCache.Clear();
        }

        public async Task FetchBroadcasterAndEntitlementsAsync()
        {
            await TwitchAuthManager.Instance.EnsureValidTokenAsync();

            var token = TwitchAuthManager.Instance.GetAccessToken();
            if (string.IsNullOrEmpty(token)) return;

            // Identity (Helix /users)
            await FetchIdentityAsync(token).ConfigureAwait(false);

            // Entitlements (backend /entitlements -> JWT)
            await RefreshEntitlementsAsync(token).ConfigureAwait(false);
        }

        private async Task FetchIdentityAsync(string userAccessToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, HelixUrl + "/users");
            req.Headers.Add("Client-Id", TwitchAuthManager.Instance.ClientId);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);

            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;

            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var json = JObject.Parse(text);
            var data = json["data"]?[0];
            if (data == null) return;

            BroadcasterId = data["id"]?.ToString();
            BroadcasterName = data["login"]?.ToString();

            Plugin.Settings.CachedBroadcasterId = BroadcasterId;
            Plugin.Settings.CachedBotUserId = BroadcasterId;
            Plugin.Settings.CachedBotUserLogin = BroadcasterName;
            Plugin.Settings.CachedBroadcasterLogin = BroadcasterName;
        }

        public async Task RefreshEntitlementsAsync(string userAccessToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, BackendEntitlementsUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);

            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                EntitlementsState.Clear();
                OnSubscriberStatusChanged?.Invoke();
                return;
            }

            var json = JObject.Parse(body);
            var entitlementToken = json["entitlementToken"]?.ToString();

            if (!TryVerifyAndParseEntitlement(entitlementToken, out var snapshot))
            {
                EntitlementsState.Clear();
                OnSubscriberStatusChanged?.Invoke();
                return;
            }

            EntitlementsState.Set(snapshot);
            Plugin.Settings.CachedSupporterTier = (int)snapshot.Tier;
            OnSubscriberStatusChanged?.Invoke();
        }

        public async Task<bool> CheckVisualsPermissionAsync()
        {
            await TwitchAuthManager.Instance.EnsureValidTokenAsync().ConfigureAwait(false);

            var accessToken = TwitchAuthManager.Instance.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken)) return false;

            // Check if entitlement is expired or missing 
            var current = EntitlementsState.Current;
            if (string.IsNullOrEmpty(current.SignedEntitlementToken) ||
                DateTime.UtcNow >= current.ExpiresAtUtc.AddMinutes(-5)) // Refresh 5 min early
            {
                Plugin.Log.Info("Entitlement token expired or missing, refreshing...");
                await RefreshEntitlementsAsync(accessToken).ConfigureAwait(false);

                // Re-check after refresh
                current = EntitlementsState.Current;
                if (string.IsNullOrEmpty(current.SignedEntitlementToken))
                {
                    Plugin.Log.Warn("Failed to refresh entitlement token");
                    return false;
                }
            }

            var entitlement = current.SignedEntitlementToken;
            // *** END CHANGE ***

            var req = new HttpRequestMessage(HttpMethod.Get, BackendBaseUrl + "/visuals/permission");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Add("X-Entitlement", entitlement);

            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                Plugin.Log.Warn($"CheckVisualsPermission failed: {resp.StatusCode} - {body}");
                return false;
            }

            try
            {
                var json = JObject.Parse(body);
                return json["allowed"]?.Value<bool>() == true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryVerifyAndParseEntitlement(string signedToken, out EntitlementsSnapshot snapshot)
        {
            snapshot = default;

            if (!JwtEd25519.TryVerify(signedToken, out var verified))
                return false;

            var payload = verified.Payload;

            // Standard claims
            long exp = payload["exp"]?.Value<long>() ?? 0;
            string sub = payload["sub"]?.ToString(); // Twitch user id on server

            if (exp <= 0) return false;
            if (string.IsNullOrEmpty(sub)) return false;

            // Ensure token is for THIS logged-in user
            string expectedUserId = TwitchAuthManager.Instance.BroadcasterId;
            if (!string.IsNullOrEmpty(expectedUserId) && !string.Equals(sub, expectedUserId, StringComparison.Ordinal))
                return false;

            // Tier claim (you set this server-side)
            int tierInt = payload["tier"]?.Value<int>() ?? 0;
            if (tierInt < 0 || tierInt > 3) return false;

            snapshot = new EntitlementsSnapshot
            {
                Tier = (SupporterTier)tierInt,
                ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime,
                SignedEntitlementToken = signedToken
            };
            return true;
        }
    }
}
