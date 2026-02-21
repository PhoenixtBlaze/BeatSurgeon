using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
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
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        private const int MaxHttpAttempts = 3;

        private TwitchApiClient() { }

        private static bool IsRetryableStatus(HttpStatusCode code)
        {
            int status = (int)code;
            return status == 429 || status >= 500;
        }

        private static async Task<Tuple<HttpStatusCode, string>> GetWithRetryAsync(
            string url,
            Action<HttpRequestHeaders> configureHeaders,
            CancellationToken ct = default(CancellationToken))
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= MaxHttpAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    configureHeaders?.Invoke(req.Headers);

                    using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeoutCts.CancelAfter(RequestTimeout);

                        try
                        {
                            using (var resp = await _http.SendAsync(req, timeoutCts.Token).ConfigureAwait(false))
                            {
                                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                if (attempt < MaxHttpAttempts && IsRetryableStatus(resp.StatusCode))
                                {
                                    await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct).ConfigureAwait(false);
                                    continue;
                                }

                                return Tuple.Create(resp.StatusCode, body);
                            }
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < MaxHttpAttempts)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct).ConfigureAwait(false);
                        }
                        catch (HttpRequestException ex) when (attempt < MaxHttpAttempts)
                        {
                            lastException = ex;
                            await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            break;
                        }
                    }
                }
            }

            if (lastException != null)
                throw lastException;

            return Tuple.Create(HttpStatusCode.ServiceUnavailable, string.Empty);
        }

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

        public async Task FetchBroadcasterAndEntitlementsAsync(CancellationToken ct = default(CancellationToken))
        {
            await TwitchAuthManager.Instance.EnsureValidTokenAsync(ct);

            var token = TwitchAuthManager.Instance.GetAccessToken();
            if (string.IsNullOrEmpty(token)) return;

            // Identity (Helix /users)
            await FetchIdentityAsync(token, ct).ConfigureAwait(false);

            // Entitlements (backend /entitlements -> JWT)
            await RefreshEntitlementsAsync(token, ct).ConfigureAwait(false);
        }

        private async Task FetchIdentityAsync(string userAccessToken, CancellationToken ct = default(CancellationToken))
        {
            var result = await GetWithRetryAsync(
                HelixUrl + "/users",
                headers =>
                {
                    headers.Add("Client-Id", TwitchAuthManager.Instance.ClientId);
                    headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
                },
                ct).ConfigureAwait(false);

            if ((int)result.Item1 < 200 || (int)result.Item1 > 299)
                return;

            var text = result.Item2;
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

        public async Task RefreshEntitlementsAsync(string userAccessToken, CancellationToken ct = default(CancellationToken))
        {
            var result = await GetWithRetryAsync(
                BackendEntitlementsUrl,
                headers => headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken),
                ct).ConfigureAwait(false);

            if ((int)result.Item1 < 200 || (int)result.Item1 > 299)
            {
                EntitlementsState.Clear();
                OnSubscriberStatusChanged?.Invoke();
                return;
            }

            var json = JObject.Parse(result.Item2);
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

        public async Task<bool> CheckVisualsPermissionAsync(CancellationToken ct = default(CancellationToken))
        {
            await TwitchAuthManager.Instance.EnsureValidTokenAsync(ct).ConfigureAwait(false);

            var accessToken = TwitchAuthManager.Instance.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken)) return false;

            // Check if entitlement is expired or missing 
            var current = EntitlementsState.Current;
            if (string.IsNullOrEmpty(current.SignedEntitlementToken) ||
                DateTime.UtcNow >= current.ExpiresAtUtc.AddMinutes(-5)) // Refresh 5 min early
            {
                Plugin.Log.Info("Entitlement token expired or missing, refreshing...");
                await RefreshEntitlementsAsync(accessToken, ct).ConfigureAwait(false);

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

            var result = await GetWithRetryAsync(
                BackendBaseUrl + "/visuals/permission",
                headers =>
                {
                    headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    headers.Add("X-Entitlement", entitlement);
                },
                ct).ConfigureAwait(false);
            var body = result.Item2;

            if ((int)result.Item1 < 200 || (int)result.Item1 > 299)
            {
                Plugin.Log.Warn($"CheckVisualsPermission failed: {result.Item1} - {body}");
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
