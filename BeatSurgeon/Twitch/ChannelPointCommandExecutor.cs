using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatSurgeon.Twitch
{
    /// <summary>
    /// Handles channel point redemption execution and automatic refunds on failure.
    /// </summary>
    internal sealed class ChannelPointCommandExecutor
    {
        private static ChannelPointCommandExecutor _instance;
        public static ChannelPointCommandExecutor Instance => _instance ?? (_instance = new ChannelPointCommandExecutor());

        private TwitchEventSubClient _eventSubClient;

        private ChannelPointCommandExecutor() { }

        /// <summary>
        /// Subscribe to channel point events from EventSub client.
        /// Call this after initializing your TwitchEventSubClient.
        /// </summary>
        public void Initialize(TwitchEventSubClient eventSubClient)
        {
            if (_eventSubClient != null)
            {
                // Unsubscribe from old client
                _eventSubClient.OnChannelPointRedeemed -= HandleChannelPointRedemption;
            }

            _eventSubClient = eventSubClient;

            if (_eventSubClient != null)
            {
                // Subscribe to new client
                _eventSubClient.OnChannelPointRedeemed += HandleChannelPointRedemption;
            }
        }

        /// <summary>
        /// Unsubscribe from events.
        /// </summary>
        public void Shutdown()
        {
            if (_eventSubClient != null)
            {
                _eventSubClient.OnChannelPointRedeemed -= HandleChannelPointRedemption;
                _eventSubClient = null;
            }
        }

        private async void HandleChannelPointRedemption(TwitchEventSubClient.ChannelPointRedemption redemption)
        {
            // Convert redemption to command using your router
            string command = ChannelPointRouter.TryBuildCommandFromReward(redemption);

            if (string.IsNullOrEmpty(command))
            {
                Plugin.Log.Debug($"Ignoring non-Beat Surgeon reward: {redemption.RewardTitle}");
                return; // Not a Beat Surgeon reward
            }

            LogUtils.Debug(() => $"Processing channel point '{command}' from {redemption.UserName}");

            try
            {
                // Execute the command
                bool success = await ExecuteCommandAsync(command, redemption.UserName);

                if (success)
                {
                    // Optional: Mark as fulfilled (you can skip this if you want)
                    try
                    {
                        await TwitchChannelPointsManager.Instance.FulfillRedemptionAsync(
                            redemption.RewardId,
                            redemption.RedemptionId,
                            CancellationToken.None
                        );
                    }
                    catch (Exception fulfillEx)
                    {
                        Plugin.Log.Warn($"Failed to mark redemption as fulfilled: {fulfillEx.Message}");
                    }

                    Plugin.Log.Info($"Successfully executed '{command}' for {redemption.UserName}");
                }
                else
                {
                    // Command failed - refund the points
                    await TwitchChannelPointsManager.Instance.RefundRedemptionAsync(
                        redemption.RewardId,
                        redemption.RedemptionId,
                        CancellationToken.None
                    );

                    Plugin.Log.Info($"Refunded '{command}' for {redemption.UserName} - command failed (not in game or invalid state)");
                }
            }
            catch (Exception ex)
            {
                // Exception occurred - refund the points
                Plugin.Log.Error($"Exception executing '{command}': {ex.Message}");

                try
                {
                    await TwitchChannelPointsManager.Instance.RefundRedemptionAsync(
                        redemption.RewardId,
                        redemption.RedemptionId,
                        CancellationToken.None
                    );

                    Plugin.Log.Info($"Refunded '{command}' for {redemption.UserName} due to exception");
                }
                catch (Exception refundEx)
                {
                    Plugin.Log.Error($"Failed to refund redemption: {refundEx.Message}");
                }
            }
        }

        /// <summary>
        /// Execute the command and return success/failure.
        /// </summary>
        private async Task<bool> ExecuteCommandAsync(string command, string userName)
        {
            // Check if player is in a song
            if (!IsInGame())
            {
                Plugin.Log.Warn($"Command '{command}' failed - not in game");
                return false;
            }

            try
            {
                // Create a ChatContext for the channel point redemption
                var context = new BeatSurgeon.Chat.ChatContext
                {
                    SenderName = userName ?? "Unknown",
                    MessageText = command,
                    IsBroadcaster = false,
                    IsModerator = false,
                    IsVip = false,
                    IsSubscriber = true, // Assume subscriber since they have channel points
                    Bits = 0,
                    Source = BeatSurgeon.Chat.ChatSource.NativeTwitch,
                    IsChannelPoint = true // IMPORTANT: Mark as channel point
                };

                // ProcessCommand now returns bool!
                bool success = BeatSurgeon.Chat.CommandHandler.Instance.ProcessCommand(command, context);

                if (success)
                {
                    Plugin.Log.Info($"Command '{command}' executed successfully for {userName}");
                }
                else
                {
                    Plugin.Log.Warn($"Command '{command}' failed for {userName}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Command execution threw exception: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Check if player is currently in a song.
        /// </summary>
        private bool IsInGame()
        {
            try
            {
                // Check 1: AudioTimeSyncController in Playing state (most reliable)
                var audio = UnityEngine.Resources.FindObjectsOfTypeAll<AudioTimeSyncController>()
                    .FirstOrDefault();

                if (audio != null && audio.state == AudioTimeSyncController.State.Playing)
                {
                    return true;
                }

                // Check 2: ScoreController exists (only present during gameplay)
                var scoreController = UnityEngine.Resources.FindObjectsOfTypeAll<ScoreController>()
                    .FirstOrDefault();

                if (scoreController != null)
                {
                    return true;
                }

                // Check 3: PauseMenuManager exists (only present during gameplay)
                var pauseMenu = UnityEngine.Resources.FindObjectsOfTypeAll<PauseMenuManager>()
                    .FirstOrDefault();

                if (pauseMenu != null)
                {
                    return true;
                }

                // Check 4: Scene name contains "GameCore"
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (currentScene.name != null && currentScene.name.Contains("GameCore"))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"IsInGame check failed: {ex.Message}");
                return false;
            }
        }
    }
}
