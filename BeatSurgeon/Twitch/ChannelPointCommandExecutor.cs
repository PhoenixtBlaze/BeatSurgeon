using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatSurgeon.Twitch
{
    /// <summary>
    /// Handles channel point redemption execution, automatic refunds on failure,
    /// and mirrors the command cooldown onto the Twitch reward itself by
    /// disabling it immediately on success and re-enabling it after the cooldown expires.
    /// </summary>
    internal sealed class ChannelPointCommandExecutor
    {
        // ── Singleton ────────────────────────────────────────────────────────────
        private static ChannelPointCommandExecutor instance;
        public static ChannelPointCommandExecutor Instance
        {
            get
            {
                if (instance == null) instance = new ChannelPointCommandExecutor(); // C# 7.3 safe
                return instance;
            }
        }

        private TwitchEventSubClient eventSubClient;
        private readonly Action<TwitchEventSubClient.ChannelPointRedemption> _channelPointRedemptionHandler;

        // Per-reward cooldown task management. Key = rewardId.
        private readonly object _rewardCooldownLock = new object();
        private readonly Dictionary<string, CancellationTokenSource> _rewardCooldownTasks
            = new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);

        private ChannelPointCommandExecutor()
        {
            _channelPointRedemptionHandler = redemption => _ = HandleChannelPointRedemptionAsync(redemption);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Subscribe to channel point events from the EventSub client.
        /// Call this after initializing your TwitchEventSubClient.
        /// </summary>
        public void Initialize(TwitchEventSubClient newClient)
        {
            if (eventSubClient != null)
                eventSubClient.OnChannelPointRedeemed -= _channelPointRedemptionHandler;

            eventSubClient = newClient;

            if (eventSubClient != null)
                eventSubClient.OnChannelPointRedeemed += _channelPointRedemptionHandler;
        }

        /// <summary>
        /// Unsubscribes events and cancels all pending reward cooldown re-enable tasks.
        /// </summary>
        public void Shutdown()
        {
            if (eventSubClient != null)
                eventSubClient.OnChannelPointRedeemed -= _channelPointRedemptionHandler;
            eventSubClient = null;

            lock (_rewardCooldownLock)
            {
                foreach (var cts in _rewardCooldownTasks.Values)
                    try { cts.Cancel(); cts.Dispose(); } catch { }
                _rewardCooldownTasks.Clear();
            }
        }

        /// <summary>
        /// Adds EventSub subscriptions for one or more newly created or re-enabled rewards.
        /// Accepts a single string, a string[], or comma-separated strings.
        /// Call from SurgeonGameplaySetupHost after a reward is created/enabled.
        /// </summary>
        public async Task ResubscribeRewardsAsync(params string[] rewardIds)
        {
            if (eventSubClient == null || rewardIds == null || rewardIds.Length == 0) return;

            // Filter out nulls/empty strings before forwarding.
            var valid = new System.Collections.Generic.List<string>();
            foreach (var id in rewardIds)
                if (!string.IsNullOrWhiteSpace(id)) valid.Add(id);

            if (valid.Count == 0) return;

            try
            {
                await eventSubClient.EnsureChannelPointSubscriptionsAsync(valid);
                Plugin.Log.Info("ChannelPointCommandExecutor: Subscribed EventSub for "
                    + valid.Count + " reward(s).");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("ChannelPointCommandExecutor: ResubscribeRewardsAsync failed: " + ex.Message);
            }
        }


        // ── Redemption handling ───────────────────────────────────────────────────

        private async Task HandleChannelPointRedemptionAsync(TwitchEventSubClient.ChannelPointRedemption redemption)
        {
            string command = null;
            try
            {
                command = ChannelPointRouter.TryBuildCommandFromReward(redemption);
                if (string.IsNullOrEmpty(command))
                {
                    Plugin.Log.Debug($"Ignoring non-Beat Surgeon reward: {redemption.RewardTitle}");
                    return;
                }

                // Use Plugin.Log.Debug directly — avoids LogUtils.Debug Func<string> overload mismatch
                Plugin.Log.Debug("ChannelPointCommandExecutor: Processing '" + command + "' from " + redemption.UserName);

                bool success = await ExecuteCommandAsync(command, redemption.UserName);

                if (success)
                {
                    // Mark fulfilled on Twitch.
                    try
                    {
                        using (var fulfillCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                        {
                            await TwitchChannelPointsManager.Instance.FulfillRedemptionAsync(
                                redemption.RewardId, redemption.RedemptionId, fulfillCts.Token);
                        }
                    }
                    catch (Exception fulfillEx)
                    {
                        Plugin.Log.Warn("ChannelPointCommandExecutor: Failed to fulfill redemption: "
                            + fulfillEx.Message);
                    }

                    // Mirror command cooldown onto the Twitch reward itself.
                    string commandKey = command.TrimStart('!').ToLowerInvariant();
                    double cooldownSeconds = BeatSurgeon.Chat.CommandHandler.GetCooldownSeconds(commandKey);

                    if (cooldownSeconds > 0.0 && !string.IsNullOrWhiteSpace(redemption.RewardId))
                    {
                        // Fire-and-forget, tracked internally for clean cancellation.
                        var _ = ApplyRewardCooldownAsync(redemption.RewardId, cooldownSeconds);
                    }
                }
                else
                {
                    // Command failed — refund the viewer.
                    try
                    {
                        using (var refundCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                        {
                            await TwitchChannelPointsManager.Instance.RefundRedemptionAsync(
                                redemption.RewardId, redemption.RedemptionId, refundCts.Token);
                        }
                        Plugin.Log.Info("ChannelPointCommandExecutor: Refunded '" + command
                            + "' for " + redemption.UserName + " — command failed.");
                    }
                    catch (Exception refundEx)
                    {
                        Plugin.Log.Error("ChannelPointCommandExecutor: Failed to refund: " + refundEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("ChannelPointCommandExecutor: Exception executing command: " + ex.Message);
                try
                {
                    using (var refundCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    {
                        await TwitchChannelPointsManager.Instance.RefundRedemptionAsync(
                            redemption.RewardId, redemption.RedemptionId, refundCts.Token);
                    }
                    Plugin.Log.Info("ChannelPointCommandExecutor: Refunded '" + command
                        + "' for " + redemption.UserName + " due to exception.");
                }
                catch (Exception refundEx)
                {
                    Plugin.Log.Error("ChannelPointCommandExecutor: Failed to refund after exception: "
                        + refundEx.Message);
                }
            }
        }

        // ── Reward cooldown ───────────────────────────────────────────────────────

        /// <summary>
        /// Disables the Twitch reward immediately, waits for the cooldown, then re-enables it.
        /// Replaces any previously pending cooldown task for the same reward.
        /// </summary>
        private async Task ApplyRewardCooldownAsync(string rewardId, double cooldownSeconds)
        {
            CancellationTokenSource newCts;
            lock (_rewardCooldownLock)
            {
                CancellationTokenSource oldCts;
                if (_rewardCooldownTasks.TryGetValue(rewardId, out oldCts))
                    try { oldCts.Cancel(); oldCts.Dispose(); } catch { }

                newCts = new CancellationTokenSource();
                _rewardCooldownTasks[rewardId] = newCts;
            }

            var ct = newCts.Token;
            try
            {
                // Disable immediately so Twitch blocks further redemptions.
                using (var disableCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(
                        rewardId, false, disableCts.Token);
                }
                Plugin.Log.Info("ChannelPointCommandExecutor: Reward " + rewardId
                    + " disabled for " + ((int)cooldownSeconds) + "s cooldown.");

                // Wait the full cooldown duration.
                await Task.Delay(TimeSpan.FromSeconds(cooldownSeconds), ct);

                // Re-enable after cooldown.
                using (var reEnableCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(
                        rewardId, true, reEnableCts.Token);
                }
                Plugin.Log.Info("ChannelPointCommandExecutor: Reward " + rewardId
                    + " re-enabled after " + ((int)cooldownSeconds) + "s.");
            }
            catch (OperationCanceledException)
            {
                // Check whether a new cooldown task already replaced this one.
                bool anotherTaskPending;
                lock (_rewardCooldownLock)
                {
                    CancellationTokenSource current;
                    anotherTaskPending = _rewardCooldownTasks.TryGetValue(rewardId, out current)
                                         && !ReferenceEquals(current, newCts);
                }

                if (!anotherTaskPending)
                {
                    // Shutdown() was called — best-effort re-enable (no-op if game is quitting,
                    // Plugin.cs DisableAllRewardsOnQuitAsync handles that path).
                    try
                    {
                        using (var shutdownReEnableCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                        {
                            await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(
                                rewardId, true, shutdownReEnableCts.Token);
                        }
                        Plugin.Log.Warn("ChannelPointCommandExecutor: Reward " + rewardId
                            + " re-enabled (cooldown interrupted by shutdown).");
                    }
                    catch (Exception reEnableEx)
                    {
                        Plugin.Log.Warn("ChannelPointCommandExecutor: Could not re-enable reward "
                            + rewardId + ": " + reEnableEx.Message);
                    }
                }
                // If anotherTaskPending == true, the new task manages disable/re-enable cleanly.
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("ChannelPointCommandExecutor: ApplyRewardCooldown failed for "
                    + rewardId + ": " + ex.Message);
                // Best-effort re-enable so reward doesn't stay stuck disabled.
                try
                {
                    using (var failReEnableCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    {
                        await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(
                            rewardId, true, failReEnableCts.Token);
                    }
                }
                catch { }
            }
            finally
            {
                lock (_rewardCooldownLock)
                {
                    CancellationTokenSource current;
                    if (_rewardCooldownTasks.TryGetValue(rewardId, out current)
                        && ReferenceEquals(current, newCts))
                        _rewardCooldownTasks.Remove(rewardId);
                }
                try { newCts.Dispose(); } catch { }
            }
        }

        // ── Command execution ─────────────────────────────────────────────────────

        private Task<bool> ExecuteCommandAsync(string command, string userName)
        {
            if (!IsInGame())
            {
                Plugin.Log.Warn("ChannelPointCommandExecutor: Command " + command + " failed — not in game.");
                return Task.FromResult(false);
            }

            try
            {
                // ChatContext is a POCO — use object initializer, not constructor named params.
                var context = new BeatSurgeon.Chat.ChatContext
                {
                    SenderName = userName ?? "Unknown",
                    MessageText = command,
                    IsBroadcaster = false,
                    IsModerator = false,
                    IsVip = false,
                    IsSubscriber = true,
                    Bits = 0,
                    Source = BeatSurgeon.Chat.ChatSource.NativeTwitch,
                    IsChannelPoint = true
                };

                bool success = BeatSurgeon.Chat.CommandHandler.Instance.ProcessCommand(command, context);
                if (success) Plugin.Log.Info("Command '" + command + "' executed for " + userName + ".");
                else Plugin.Log.Warn("Command '" + command + "' failed for " + userName + ".");
                return Task.FromResult(success);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("ChannelPointCommandExecutor: Command execution threw exception: "
                    + ex.Message);
                return Task.FromResult(false);
            }
        }

        private bool IsInGame()
        {
            try
            {
                var audio = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().FirstOrDefault();
                if (audio != null && audio.state == AudioTimeSyncController.State.Playing) return true;
                if (Resources.FindObjectsOfTypeAll<ScoreController>().FirstOrDefault() != null) return true;
                if (Resources.FindObjectsOfTypeAll<PauseMenuManager>().FirstOrDefault() != null) return true;
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.name != null && scene.name.Contains("GameCore")) return true;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("ChannelPointCommandExecutor: IsInGame check failed: " + ex.Message);
                return false;
            }
        }
    }
}
