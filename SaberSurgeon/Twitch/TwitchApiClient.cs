using Newtonsoft.Json.Linq;
using SaberSurgeon.UI.Controllers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace SaberSurgeon.Twitch
{
    public class TwitchApiClient
    {
        public static event Action OnSubscriberStatusChanged;

        private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

        private static TwitchApiClient _instance;
        public static TwitchApiClient Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new TwitchApiClient();
                return _instance;
            }
        }

        public string BroadcasterId { get; private set; }
        public string BroadcasterName { get; private set; }
        public string SupportChannelId { get; private set; }

        private const string HelixUrl = "https://api.twitch.tv/helix";

        private const string BackendEntitlementsUrl = "https://phoenixblaze0.duckdns.org/entitlements";

        public static IEnumerator GetSpriteFromUrl(string url, Action<Sprite> callback)
        {
            if (string.IsNullOrEmpty(url))
            {
                callback?.Invoke(null);
                yield break;
            }

            // 1. Check Cache
            if (_spriteCache.TryGetValue(url, out var cachedSprite))
            {
                if (cachedSprite != null)
                {
                    callback?.Invoke(cachedSprite);
                    yield break;
                }
                else
                {
                    _spriteCache.Remove(url); // Clean dead entry
                }
            }

            // 2. Download
            using (var www = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    var texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(www);
                    if (texture != null)
                    {
                        // Create Sprite
                        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));

                        // Add to cache
                        _spriteCache[url] = sprite;

                        callback?.Invoke(sprite);
                    }
                }
                else
                {
                    // Log error sparingly
                    callback?.Invoke(null);
                }
            }
        }

        // Call this on Plugin.OnApplicationQuit or Level Change to free memory
        public static void ClearCache()
        {
            foreach (var sprite in _spriteCache.Values)
            {
                if (sprite != null && sprite.texture != null)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
            }
            _spriteCache.Clear();
        }


        private async Task CheckEntitlementsAsync(HttpClient client, string token)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, BackendEntitlementsUrl);
            req.Headers.Add("Authorization", "Bearer " + token);

            var resp = await client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                Plugin.Log.Warn($"TwitchAPI: /entitlements failed status={resp.StatusCode} body={body}");
                return;
            }

            var json = JObject.Parse(body);
            var payload = json["payload"];

            bool supporter =
                payload?["features"]?["supporter"]?.Value<bool>() ?? false;

            // Map boolean entitlement to your existing tier model
            Plugin.Settings.CachedSupporterTier = supporter ? 1 : 0;
            SupporterState.CurrentTier = supporter ? SupporterTier.Tier1 : SupporterTier.None;

            OnSubscriberStatusChanged?.Invoke();
        }



        public async Task FetchBroadcasterAndSupportInfo()
        {
            string token = TwitchAuthManager.Instance.GetAccessToken();
            if (string.IsNullOrEmpty(token)) return;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Client-Id", TwitchAuthManager.Instance.ClientId);
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

                // 1. Get Broadcaster Info
                var userRes = await client.GetAsync(HelixUrl + "/users");
                Plugin.Log.Info($"TwitchAPI: /users status={userRes.StatusCode}");
                if (userRes.IsSuccessStatusCode)
                {
                    var text = await userRes.Content.ReadAsStringAsync();
                    Plugin.Log.Info($"TwitchAPI: /users body={text}");
                    var json = JObject.Parse(text);
                    var data = json["data"]?[0];

                    if (data != null)
                    {
                        BroadcasterId = data["id"]?.ToString();
                        BroadcasterName = data["login"]?.ToString();
                        Plugin.Log.Info($"TwitchAPI: Raw user data id={BroadcasterId}, login={BroadcasterName}");

                        Plugin.Settings.CachedBroadcasterId = BroadcasterId;
                        Plugin.Settings.CachedBotUserId = BroadcasterId;
                        Plugin.Settings.CachedBotUserLogin = BroadcasterName;
                        Plugin.Settings.CachedBroadcasterLogin = BroadcasterName;
                        Plugin.Log.Info("TwitchAPI: Logged in as " + BroadcasterName);
                    }
                    else
                    {
                        Plugin.Log.Warn("TwitchAPI: /users returned no data array.");
                    }
                }
                else
                {
                    var errBody = await userRes.Content.ReadAsStringAsync();
                    Plugin.Log.Warn($"TwitchAPI: /users failed status={userRes.StatusCode} body={errBody}");
                }

                await CheckEntitlementsAsync(client, token);
                // 2. Get Support Channel Info
                var supportRes = await client.GetAsync(HelixUrl + "/users?login=" + TwitchAuthManager.SupportChannelName);
                if (supportRes.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(await supportRes.Content.ReadAsStringAsync());
                    var data = json["data"]?[0];
                    if (data != null)
                    {
                        SupportChannelId = data["id"]?.ToString();
                        Plugin.Log.Info("TwitchAPI: Support Channel ID resolved: " + SupportChannelId);
                    }
                }

                // 3. Check Subscription
                if (!string.IsNullOrEmpty(BroadcasterId) && !string.IsNullOrEmpty(SupportChannelId))
                {
                    await CheckSupporterStatus(client, token);
                }
                Plugin.Log.Info($"TwitchAPI: Finished Helix fetch. Name={BroadcasterName}, Tier={Plugin.Settings.CachedSupporterTier}");
            }
        }

        private async Task CheckSupporterStatus(HttpClient client, string token)
        {
            string url = HelixUrl + "/subscriptions/user?broadcaster_id=" + SupportChannelId + "&user_id=" + BroadcasterId;
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var data = json["data"]?[0];
                if (data != null)
                {
                    string tierString = data["tier"]?.ToString();

                    int tier = 0;
                    if (tierString == "1000") tier = 1;
                    else if (tierString == "2000") tier = 2;
                    else if (tierString == "3000") tier = 3;

                    Plugin.Settings.CachedSupporterTier = tier;
                    SupporterState.CurrentTier = (SupporterTier)tier;

                    Plugin.Log.Info("TwitchAPI: User is Tier " + tier + " Supporter!");


                }
                // Invoke the static event so any listening ViewControllers can update
                OnSubscriberStatusChanged?.Invoke();
                Plugin.Log.Info("TwitchAPI: Subscriber status changed event fired ");


            }
            else
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Plugin.Log.Info("TwitchAPI: Not subscribed.");
                    Plugin.Settings.CachedSupporterTier = 0;
                    SupporterState.CurrentTier = SupporterTier.None;
                }
                else
                {
                    Plugin.Log.Warn("TwitchAPI: Failed to check sub status. Code: " + response.StatusCode);
                }
            }
        }
    }
}