using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class TwitchChannelPointsManager : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TwitchChannelPointsManager");
        private static TwitchChannelPointsManager _instance;

        internal sealed class RewardSpec
        {
            internal string Key;
            internal string Title;
            internal string Prompt;
            internal int Cost;
            internal int CooldownSeconds;
            internal bool ApplyCooldown;
            internal string BackgroundColorHex;
        }

        internal sealed class RewardRecord
        {
            internal RewardRecord(string rewardId, string title, bool isEnabled, bool isOwned)
            {
                RewardId = rewardId;
                Title = title;
                IsEnabled = isEnabled;
                IsOwned = isOwned;
            }

            internal string RewardId { get; private set; }
            internal string Title { get; private set; }
            internal bool IsEnabled { get; private set; }
            internal bool IsOwned { get; private set; }

            internal RewardRecord WithEnabled(bool enabled)
                => new RewardRecord(RewardId, Title, enabled, IsOwned);
        }

        private readonly TwitchApiClient _apiClient;
        private readonly TwitchEventSubClient _eventSubClient;
        private readonly TwitchAuthManager _authManager;

        private readonly ConcurrentDictionary<string, RewardRecord> _rewards =
            new ConcurrentDictionary<string, RewardRecord>(StringComparer.Ordinal);

        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _shutdownCts;

        internal static TwitchChannelPointsManager Instance =>
            _instance ?? (_instance = new TwitchChannelPointsManager(
                TwitchApiClient.Instance,
                TwitchEventSubClient.Instance,
                TwitchAuthManager.Instance));

        [Inject]
        public TwitchChannelPointsManager(
            TwitchApiClient apiClient,
            TwitchEventSubClient eventSubClient,
            TwitchAuthManager authManager)
        {
            _instance = this;
            _apiClient = apiClient;
            _eventSubClient = eventSubClient;
            _authManager = authManager;
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize");
            _shutdownCts = new CancellationTokenSource();
            Application.quitting += OnApplicationQuitting;
            _log.Info("Registered Application.quitting handler");

            _authManager.OnAuthReady += HandleAuthReady;

            // If auth is already ready (valid cached token + identity loaded), restore immediately.
            if (_authManager.IsAuthenticated && !string.IsNullOrWhiteSpace(_authManager.BroadcasterId))
            {
                _ = AutoRestoreEnabledRewardsAsync(_shutdownCts.Token);
            }
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose");
            Application.quitting -= OnApplicationQuitting;
            _authManager.OnAuthReady -= HandleAuthReady;
            _shutdownCts?.Dispose();
        }

        private void HandleAuthReady()
        {
            _ = AutoRestoreEnabledRewardsAsync(_shutdownCts?.Token ?? CancellationToken.None);
        }

        private async Task AutoRestoreEnabledRewardsAsync(CancellationToken ct)
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null) return;

            var toRestore = new List<string>(8);
            if (cfg.CpRainbowEnabled && !string.IsNullOrWhiteSpace(cfg.CpRainbowRewardId)) toRestore.Add(cfg.CpRainbowRewardId);
            if (cfg.CpDisappearEnabled && !string.IsNullOrWhiteSpace(cfg.CpDisappearRewardId)) toRestore.Add(cfg.CpDisappearRewardId);
            if (cfg.CpGhostEnabled && !string.IsNullOrWhiteSpace(cfg.CpGhostRewardId)) toRestore.Add(cfg.CpGhostRewardId);
            if (cfg.CpBombEnabled && !string.IsNullOrWhiteSpace(cfg.CpBombRewardId)) toRestore.Add(cfg.CpBombRewardId);
            if (cfg.CpFasterEnabled && !string.IsNullOrWhiteSpace(cfg.CpFasterRewardId)) toRestore.Add(cfg.CpFasterRewardId);
            if (cfg.CpSuperFastEnabled && !string.IsNullOrWhiteSpace(cfg.CpSuperFastRewardId)) toRestore.Add(cfg.CpSuperFastRewardId);
            if (cfg.CpSlowerEnabled && !string.IsNullOrWhiteSpace(cfg.CpSlowerRewardId)) toRestore.Add(cfg.CpSlowerRewardId);
            if (cfg.CpFlashbangEnabled && !string.IsNullOrWhiteSpace(cfg.CpFlashbangRewardId)) toRestore.Add(cfg.CpFlashbangRewardId);

            if (toRestore.Count == 0) return;

            _log.Info("AutoRestoreEnabledRewards: re-enabling " + toRestore.Count + " configured CP rewards on startup");
            try
            {
                string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
                foreach (string rewardId in toRestore)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        await _apiClient.SetRewardEnabledAsync(channelUserId, rewardId, true, ct).ConfigureAwait(false);
                        if (!_rewards.ContainsKey(rewardId))
                        {
                            _rewards[rewardId] = new RewardRecord(rewardId, string.Empty, isEnabled: true, isOwned: true);
                        }
                        _log.ChannelPoint(rewardId, "AutoRestored", "re-enabled on startup");
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex, "AutoRestoreEnabledRewards rewardId=" + rewardId);
                    }
                }
                _log.Info("AutoRestoreEnabledRewards complete");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "AutoRestoreEnabledRewardsAsync");
            }
        }

        private void OnApplicationQuitting()
        {
            _log.Lifecycle("OnApplicationQuitting - disabling all owned CP rewards");
            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    DisableAllOwnedRewardsAsync(timeoutCts.Token).GetAwaiter().GetResult();
                }
                _log.Info("All owned CP rewards disabled on quit");
            }
            catch (OperationCanceledException)
            {
                _log.Warn("DisableAllOwnedRewards timed out during quit (>5s) - some rewards may remain enabled");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnApplicationQuitting");
            }
        }

        internal async Task<string> CreateRewardAsync(
            string title,
            int cost,
            CancellationToken ct = default(CancellationToken))
        {
            _log.ChannelPoint("NEW", "CreateRewardStarted", "title=" + title + " cost=" + cost);

            await _operationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
                string rewardId = await _apiClient.CreateCustomRewardAsync(channelUserId, title, cost, ct).ConfigureAwait(false);

                _rewards[rewardId] = new RewardRecord(rewardId, title, isEnabled: true, isOwned: true);
                _log.ChannelPoint(rewardId, "RewardCreated", "title=" + title);

                await _eventSubClient.SubscribeToRewardAsync(rewardId, channelUserId, ct).ConfigureAwait(false);
                _log.ChannelPoint(rewardId, "EventSubSubscribed", "immediately after creation");

                return rewardId;
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "CreateRewardAsync title=" + title);
                throw;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        internal async Task<string> CreateRewardAsync(RewardSpec spec, bool enabled, CancellationToken ct)
        {
            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
            JObject created = await _apiClient.CreateCustomRewardAsync(
                channelUserId,
                spec.Title,
                spec.Prompt,
                spec.Cost,
                spec.CooldownSeconds,
                spec.BackgroundColorHex,
                enabled,
                ct).ConfigureAwait(false);

            string rewardId = created?["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(rewardId))
            {
                throw new InvalidOperationException("CreateRewardAsync: Twitch did not return reward id.");
            }

            bool rewardEnabled = created["is_enabled"]?.Value<bool?>() ?? enabled;
            _rewards[rewardId] = new RewardRecord(rewardId, spec.Title, rewardEnabled, isOwned: true);
            return rewardId;
        }

        internal async Task SetRewardEnabledAsync(string rewardId, bool enabled, CancellationToken ct = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(rewardId))
            {
                return;
            }

            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
            await _apiClient.SetRewardEnabledAsync(channelUserId, rewardId, enabled, ct).ConfigureAwait(false);

            if (_rewards.TryGetValue(rewardId, out RewardRecord existing))
            {
                _rewards[rewardId] = existing.WithEnabled(enabled);
            }
        }

        internal async Task FulfillRedemptionAsync(string rewardId, string redemptionId, CancellationToken ct)
        {
            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
            await _apiClient.UpdateRedemptionStatusAsync(channelUserId, rewardId, redemptionId, "FULFILLED", ct).ConfigureAwait(false);
        }

        internal async Task RefundRedemptionAsync(string rewardId, string redemptionId, CancellationToken ct)
        {
            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
            await _apiClient.UpdateRedemptionStatusAsync(channelUserId, rewardId, redemptionId, "CANCELED", ct).ConfigureAwait(false);
        }

        internal async Task<JArray> GetManageableRewardsAsync(CancellationToken ct)
        {
            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
            return await _apiClient.GetManageableRewardsAsync(channelUserId, ct).ConfigureAwait(false);
        }

        internal async Task<string> EnsureRewardAsync(
            RewardSpec spec,
            string storedRewardId,
            Action<string> saveRewardId,
            bool enabled,
            CancellationToken ct)
        {
            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
            JArray rewards = await _apiClient.GetManageableRewardsAsync(channelUserId, ct).ConfigureAwait(false);
            JObject found = null;

            string storedId = (storedRewardId ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(storedId))
            {
                found = rewards.OfType<JObject>().FirstOrDefault(x => string.Equals(x["id"]?.ToString(), storedId, StringComparison.Ordinal));
            }

            if (found == null)
            {
                found = rewards.OfType<JObject>().FirstOrDefault(x => string.Equals(x["title"]?.ToString(), spec.Title, StringComparison.Ordinal));
            }

            if (found == null)
            {
                if (!enabled)
                {
                    saveRewardId?.Invoke(string.Empty);
                    return string.Empty;
                }

                string created = await CreateRewardAsync(spec, enabled, ct).ConfigureAwait(false);
                saveRewardId?.Invoke(created);
                if (enabled)
                {
                    await _eventSubClient.SubscribeToRewardAsync(created, channelUserId, ct).ConfigureAwait(false);
                }
                return created;
            }

            string id = found["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("EnsureRewardAsync: Twitch reward missing id.");
            }

            saveRewardId?.Invoke(id);

            JObject updated = await _apiClient.UpdateCustomRewardAsync(
                channelUserId,
                id,
                spec.Title,
                spec.Prompt,
                spec.Cost,
                spec.CooldownSeconds,
                spec.BackgroundColorHex,
                enabled,
                applyCooldown: spec.ApplyCooldown,
                ct: ct).ConfigureAwait(false);

            bool rewardEnabled = updated?["is_enabled"]?.Value<bool?>()
                ?? found["is_enabled"]?.Value<bool?>()
                ?? enabled;
            _rewards[id] = new RewardRecord(id, spec.Title, rewardEnabled, true);

            if (enabled)
            {
                await _eventSubClient.SubscribeToRewardAsync(id, channelUserId, ct).ConfigureAwait(false);
            }
            else
            {
                await _eventSubClient.UnsubscribeFromRewardAsync(id, ct).ConfigureAwait(false);
            }

            return id;
        }

        internal async Task CreateAllConfiguredRewardsAsync(CancellationToken ct = default(CancellationToken))
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return;
            }

            if (cfg.CpRainbowEnabled)
            {
                cfg.CpRainbowRewardId = await CreateRewardAsync("Rainbow Notes", cfg.CpRainbowCost, ct).ConfigureAwait(false);
            }
            if (cfg.CpGhostEnabled)
            {
                cfg.CpGhostRewardId = await CreateRewardAsync("Ghost Notes", cfg.CpGhostCost, ct).ConfigureAwait(false);
            }
            if (cfg.CpDisappearEnabled)
            {
                cfg.CpDisappearRewardId = await CreateRewardAsync("Disappearing Arrows", cfg.CpDisappearCost, ct).ConfigureAwait(false);
            }
            if (cfg.CpBombEnabled)
            {
                cfg.CpBombRewardId = await CreateRewardAsync("Bomb Note", cfg.CpBombCost, ct).ConfigureAwait(false);
            }
            if (cfg.CpFasterEnabled)
            {
                cfg.CpFasterRewardId = await CreateRewardAsync("Faster Song", cfg.CpFasterCost, ct).ConfigureAwait(false);
            }
            if (cfg.CpSuperFastEnabled)
            {
                cfg.CpSuperFastRewardId = await CreateRewardAsync("SuperFast Song", cfg.CpSuperFastCost, ct).ConfigureAwait(false);
            }
            if (cfg.CpSlowerEnabled)
            {
                cfg.CpSlowerRewardId = await CreateRewardAsync("Slower Song", cfg.CpSlowerCost, ct).ConfigureAwait(false);
            }
            if (cfg.CpFlashbangEnabled)
            {
                cfg.CpFlashbangRewardId = await CreateRewardAsync("Flashbang", cfg.CpFlashbangCost, ct).ConfigureAwait(false);
            }
        }

        internal IReadOnlyCollection<string> GetTrackedRewardIdsSnapshot()
        {
            return _rewards.Keys.ToList();
        }

        private async Task DisableAllOwnedRewardsAsync(CancellationToken ct)
        {
            List<RewardRecord> ownedRewards = _rewards.Values.Where(r => r.IsOwned && r.IsEnabled).ToList();
            _log.Info("Disabling " + ownedRewards.Count + " owned rewards");

            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
            foreach (RewardRecord reward in ownedRewards)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await _apiClient.SetRewardEnabledAsync(channelUserId, reward.RewardId, false, ct).ConfigureAwait(false);
                    if (_rewards.TryGetValue(reward.RewardId, out RewardRecord existing))
                    {
                        _rewards[reward.RewardId] = existing.WithEnabled(false);
                    }

                    _log.ChannelPoint(reward.RewardId, "DisabledOK", "title=" + reward.Title);
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "DisableReward rewardId=" + reward.RewardId);
                }
            }
        }
    }
}
