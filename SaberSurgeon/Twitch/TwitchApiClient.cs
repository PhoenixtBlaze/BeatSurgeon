using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SaberSurgeon.Twitch
{
    public class TwitchApiClient
    {
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
        //private const string ClientId = "dyq6orcrvl9cxd8d1usx6rtczt3tfb";

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