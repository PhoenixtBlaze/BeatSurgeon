using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeatSurgeon.Twitch
{
    internal sealed class TwitchChannelPointsManager
    {
        private const string HelixBase = "https://api.twitch.tv/helix";
        private static readonly HttpClient _http = new HttpClient();

        private static TwitchChannelPointsManager _instance;
        public static TwitchChannelPointsManager Instance => _instance ?? (_instance = new TwitchChannelPointsManager());

        private TwitchChannelPointsManager() { }

        internal sealed class RewardSpec
        {
            public string Key;
            public string Title;
            public string Prompt;
            public int Cost;
            public int CooldownSeconds; // 0 = no cooldown

            public string BackgroundColorHex; // "#RRGGBB" (optional)
        }

        private async Task<HttpClient> CreateAuthedClientAsync(CancellationToken ct)
        {
            await TwitchAuthManager.Instance.EnsureReadyAsync();
            ct.ThrowIfCancellationRequested();

            string token = TwitchAuthManager.Instance.GetAccessToken();
            string clientId = TwitchAuthManager.Instance.ClientId;

            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("No Twitch access token.");

            var client = _http;

            client.DefaultRequestHeaders.Remove("Client-Id");
            client.DefaultRequestHeaders.Remove("Authorization");
            client.DefaultRequestHeaders.Add("Client-Id", clientId);
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

            return client;
        }

        private static string RequireBroadcasterId()
        {
            string broadcasterId = TwitchAuthManager.Instance.BroadcasterId;
            if (string.IsNullOrEmpty(broadcasterId))
                throw new InvalidOperationException("BroadcasterId not resolved yet.");
            return broadcasterId;
        }

        public async Task<JArray> GetManageableRewardsAsync(CancellationToken ct)
        {
            var client = await CreateAuthedClientAsync(ct);
            string broadcasterId = RequireBroadcasterId();

            string url =
                $"{HelixBase}/channel_points/custom_rewards" +
                $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}" +
                $"&only_manageable_rewards=true";

            var resp = await client.GetAsync(url, ct);
            string body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"GetManageableRewards failed HTTP={(int)resp.StatusCode} body={body}");

            var json = JObject.Parse(body);
            return (JArray)(json["data"] ?? new JArray());
        }

        public async Task<string> CreateRewardAsync(RewardSpec spec, bool enabled, CancellationToken ct)
        {
            var client = await CreateAuthedClientAsync(ct);
            string broadcasterId = RequireBroadcasterId();

            var payload = new JObject
            {
                ["title"] = spec.Title,
                ["cost"] = Math.Max(1, spec.Cost),
                ["prompt"] = spec.Prompt ?? "",
                ["is_enabled"] = enabled,
                ["is_user_input_required"] = false,
                ["is_max_per_stream_enabled"] = false,
                ["is_max_per_user_per_stream_enabled"] = false,
            };

            if (!string.IsNullOrWhiteSpace(spec.BackgroundColorHex))
                payload["background_color"] = spec.BackgroundColorHex;

            if (spec.CooldownSeconds > 0)
            {
                payload["is_global_cooldown_enabled"] = true;
                payload["global_cooldown_seconds"] = Math.Min(604800, Math.Max(1, spec.CooldownSeconds));
            }
            else
            {
                payload["is_global_cooldown_enabled"] = false;
                // IMPORTANT: do NOT include global_cooldown_seconds when disabled
            }

            string url =
                $"{HelixBase}/channel_points/custom_rewards" +
                $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}";

            var resp = await client.PostAsync(url, new StringContent(payload.ToString(), Encoding.UTF8, "application/json"), ct);
            string body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"CreateReward failed HTTP={(int)resp.StatusCode} body={body}");

            var json = JObject.Parse(body);
            var id = json["data"]?[0]?["id"]?.ToString();

            if (string.IsNullOrEmpty(id))
                throw new Exception("CreateReward succeeded but response had no reward id.");

            return id;
        }

        public async Task UpdateRewardAsync(string rewardId, RewardSpec spec, bool enabled, CancellationToken ct)
        {
            var client = await CreateAuthedClientAsync(ct);
            string broadcasterId = RequireBroadcasterId();

            var payload = new JObject
            {
                ["title"] = spec.Title,
                ["cost"] = Math.Max(1, spec.Cost),
                ["prompt"] = spec.Prompt ?? "",
                ["is_enabled"] = enabled,
            };

            if (!string.IsNullOrWhiteSpace(spec.BackgroundColorHex))
                payload["background_color"] = spec.BackgroundColorHex;

            if (spec.CooldownSeconds > 0)
            {
                payload["is_global_cooldown_enabled"] = true;
                payload["global_cooldown_seconds"] = Math.Min(604800, Math.Max(1, spec.CooldownSeconds));
            }
            else
            {
                payload["is_global_cooldown_enabled"] = false;

                // Still include the seconds field to satisfy Helix validation.
                // Use 1 to avoid “minimum is 1” validation edge cases.
                payload["global_cooldown_seconds"] = 1;
            }

            string url =
                $"{HelixBase}/channel_points/custom_rewards" +
                $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}" +
                $"&id={Uri.EscapeDataString(rewardId)}";

            var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json")
            };

            var resp = await client.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"UpdateReward failed HTTP={(int)resp.StatusCode} body={body}");
        }

        /// <summary>
        /// Lightweight enable/disable without rewriting title/cost/prompt.
        /// This fixes your Plugin.cs build error (missing SetRewardEnabledAsync). [file:221]
        /// </summary>
        public async Task SetRewardEnabledAsync(string rewardId, bool enabled, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(rewardId))
                return;

            var client = await CreateAuthedClientAsync(ct);
            string broadcasterId = RequireBroadcasterId();

            var payload = new JObject
            {
                ["is_enabled"] = enabled
            };

            string url =
                $"{HelixBase}/channel_points/custom_rewards" +
                $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}" +
                $"&id={Uri.EscapeDataString(rewardId)}";

            var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json")
            };

            var resp = await client.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"SetRewardEnabled failed HTTP={(int)resp.StatusCode} body={body}");
        }


        /// <summary>
        /// Updates the status of a custom reward redemption.
        /// Status can be "FULFILLED" or "CANCELED" (CANCELED refunds the points).
        /// Requires channel:manage:redemptions scope.
        /// </summary>
        public async Task UpdateRedemptionStatusAsync(
            string rewardId,
            string redemptionId,
            string status, // "FULFILLED" or "CANCELED"
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(rewardId))
                throw new ArgumentException("Reward ID is required", nameof(rewardId));

            if (string.IsNullOrWhiteSpace(redemptionId))
                throw new ArgumentException("Redemption ID is required", nameof(redemptionId));

            if (status != "FULFILLED" && status != "CANCELED")
                throw new ArgumentException("Status must be FULFILLED or CANCELED", nameof(status));

            var client = await CreateAuthedClientAsync(ct);
            string broadcasterId = RequireBroadcasterId();

            var payload = new JObject
            {
                ["status"] = status
            };

            string url =
                $"{HelixBase}/channel_points/custom_rewards/redemptions" +
                $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}" +
                $"&reward_id={Uri.EscapeDataString(rewardId)}" +
                $"&id={Uri.EscapeDataString(redemptionId)}";

            var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json")
            };

            var resp = await client.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"UpdateRedemptionStatus failed HTTP={(int)resp.StatusCode} body={body}");
        }

        /// <summary>
        /// Convenience method to cancel/refund a redemption.
        /// </summary>
        public async Task RefundRedemptionAsync(string rewardId, string redemptionId, CancellationToken ct)
        {
            await UpdateRedemptionStatusAsync(rewardId, redemptionId, "CANCELED", ct);
        }

        /// <summary>
        /// Convenience method to fulfill a redemption.
        /// </summary>
        public async Task FulfillRedemptionAsync(string rewardId, string redemptionId, CancellationToken ct)
        {
            await UpdateRedemptionStatusAsync(rewardId, redemptionId, "FULFILLED", ct);
        }

        /// <summary>
        /// Signature that matches your SurgeonGameplaySetupHost call site (storedRewardId/saveRewardId). [file:223]
        /// </summary>
        public async Task<string> EnsureRewardAsync(
            RewardSpec spec,
            string storedRewardId,
            Action<string> saveRewardId,
            bool enabled,
            CancellationToken ct)
        {
            string storedId = (storedRewardId ?? "").Trim();
            var rewards = await GetManageableRewardsAsync(ct);

            JObject found = null;

            if (!string.IsNullOrEmpty(storedId))
            {
                foreach (var r in rewards)
                {
                    if (r?["id"]?.ToString() == storedId)
                    {
                        found = (JObject)r;
                        break;
                    }
                }
            }

            if (found == null)
            {
                foreach (var r in rewards)
                {
                    if (string.Equals(r?["title"]?.ToString(), spec.Title, StringComparison.Ordinal))
                    {
                        found = (JObject)r;
                        break;
                    }
                }
            }

            if (found == null)
            {
                string newId = await CreateRewardAsync(spec, enabled, ct);
                saveRewardId?.Invoke(newId);
                return newId;
            }

            string id = found["id"]?.ToString();
            if (!string.IsNullOrEmpty(id))
                saveRewardId?.Invoke(id);

            await UpdateRewardAsync(id, spec, enabled, ct);
            return id;
        }
    }
}
