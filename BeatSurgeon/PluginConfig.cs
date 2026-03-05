using BeatSurgeon.Utils;
using IPA.Config.Stores.Attributes;
using UnityEngine;

namespace BeatSurgeon
{
    // Must be public or internal with public virtual properties for BSIPA Generated<T>()
    public class PluginConfig
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("PluginConfig");
        private long _tokenExpiryTicks;

        public static PluginConfig Instance { get; set; }

        // --- Auth (PRD canonical fields) ---
        public virtual string AccessToken { get; set; } = string.Empty;
        public virtual string RefreshToken { get; set; } = string.Empty;
        public virtual string ClientId { get; set; } = "dyq6orcrvl9cxd8d1usx6rtczt3tfb";
        public virtual string ChannelUserId { get; set; } = string.Empty;
        public virtual long TokenExpiryTicks
        {
            get => _tokenExpiryTicks;
            set => _tokenExpiryTicks = value;
        }

        [Ignore]
        public System.DateTime TokenExpiry
        {
            get => _tokenExpiryTicks > 0
                ? new System.DateTime(_tokenExpiryTicks, System.DateTimeKind.Utc)
                : System.DateTime.MinValue;
            set => _tokenExpiryTicks = value == System.DateTime.MinValue
                ? 0
                : value.ToUniversalTime().Ticks;
        }

        [Ignore]
        public bool HasValidToken =>
            !string.IsNullOrEmpty(AccessToken) &&
            TokenExpiry > System.DateTime.UtcNow.AddMinutes(1);

        // --- Commands / Toggles ---
        public virtual string BombCommandName { get; set; } = "bomb";

        public virtual bool RainbowEnabled { get; set; } = true;
        public virtual float RainbowCycleSpeed { get; set; } = 0.1f;
        public virtual bool RainbowGradientFadeEnabled { get; set; } = true;
        public virtual bool DisappearEnabled { get; set; } = true;
        public virtual bool GhostEnabled { get; set; } = true;
        public virtual bool BombEnabled { get; set; } = true;
        public virtual float BombTextHeight { get; set; } = 1.0f;
        public virtual float BombTextWidth { get; set; } = 1.0f;
        public virtual float BombSpawnDistance { get; set; } = 20.0f;
        public virtual string BombFontType { get; set; } = "Default";
        public virtual string BombFireworksTextureType { get; set; }
        public virtual Color BombGradientStart { get; set; } = Color.blue;
        public virtual Color BombGradientEnd { get; set; } = Color.red;
        public virtual bool FasterEnabled { get; set; } = false;
        public virtual bool SuperFastEnabled { get; set; } = false;
        public virtual bool SlowerEnabled { get; set; } = true;
        public virtual bool FlashbangEnabled { get; set; } = true;
        public virtual int FlashbangBrightnessMultiplier { get; set; } = 90;

        // --- Cooldowns ---
        public virtual bool GlobalCooldownEnabled { get; set; } = false;
        public virtual bool PerCommandCooldownsEnabled { get; set; } = true;
        public virtual float GlobalCooldownSeconds { get; set; } = 60f;

        public virtual float RainbowCooldownSeconds { get; set; } = 60f;
        public virtual float DisappearCooldownSeconds { get; set; } = 60f;
        public virtual float GhostCooldownSeconds { get; set; } = 60f;
        public virtual float BombCooldownSeconds { get; set; } = 60f;
        public virtual float FasterCooldownSeconds { get; set; } = 60f;
        public virtual float SuperFastCooldownSeconds { get; set; } = 60f;
        public virtual float SlowerCooldownSeconds { get; set; } = 60f;
        public virtual float FlashbangCooldownSeconds { get; set; } = 60f;

        public virtual bool SpeedExclusiveEnabled { get; set; } = true;

        // --- Legacy encrypted auth fields (backwards compatibility) ---
        public virtual string EncryptedAccessToken { get; set; } = string.Empty;
        public virtual string EncryptedRefreshToken { get; set; } = string.Empty;

        // --- Twitch / backend settings ---
        public virtual string CachedBroadcasterId { get; set; } = string.Empty;
        public virtual string CachedBroadcasterLogin { get; set; } = string.Empty;
        public virtual int CachedSupporterTier { get; set; } = 0;
        public virtual bool PreferNativeTwitchBackend { get; set; } = true;
        public virtual bool AllowChatPlexFallback { get; set; } = true;
        public virtual string BackendStatus { get; set; } = string.Empty;
        public virtual string CachedBotUserId { get; set; } = string.Empty;
        public virtual string CachedBotUserLogin { get; set; } = string.Empty;
        public virtual bool AutoConnectTwitch { get; set; } = true;
        public virtual bool TwitchReauthRequired { get; set; } = false;

        // --- Endless / Song Requests ---
        public virtual bool SongRequestsEnabled { get; set; } = true;
        public virtual bool RequestAllowSpecificDifficulty { get; set; } = true;
        public virtual bool RequestAllowSpecificTime { get; set; } = true;
        public virtual int QueueSizeLimit { get; set; } = 20;
        public virtual int RequeueLimit { get; set; } = 10;
        public virtual bool BsrCommandAliasEnabled { get; set; } = true;
        public virtual bool PlayFirstSubmitLaterEnabled { get; set; } = true;
        public virtual bool ScoreSubmissionEnabled { get; set; } = true;
        public virtual bool AutoPauseOnMapEnd { get; set; } = true;
        public virtual bool DebugMode { get; set; } = false;

        // --- Multiplayer ---
        public virtual string MpClientId { get; set; } = string.Empty;

        // --- Channel Points ---
        public virtual bool CpRainbowEnabled { get; set; } = false;
        public virtual int CpRainbowCost { get; set; } = 500;
        public virtual int CpRainbowCooldownSeconds { get; set; } = 0;
        public virtual string CpRainbowRewardId { get; set; } = string.Empty;
        public virtual Color CpRainbowBackgroundColor { get; set; } = Color.white;

        public virtual bool CpDisappearEnabled { get; set; } = false;
        public virtual int CpDisappearCost { get; set; } = 500;
        public virtual int CpDisappearCooldownSeconds { get; set; } = 0;
        public virtual string CpDisappearRewardId { get; set; } = string.Empty;
        public virtual Color CpDisappearBackgroundColor { get; set; } = Color.white;

        public virtual bool CpGhostEnabled { get; set; } = false;
        public virtual int CpGhostCost { get; set; } = 500;
        public virtual int CpGhostCooldownSeconds { get; set; } = 0;
        public virtual string CpGhostRewardId { get; set; } = string.Empty;
        public virtual Color CpGhostBackgroundColor { get; set; } = Color.white;

        public virtual bool CpBombEnabled { get; set; } = false;
        public virtual int CpBombCost { get; set; } = 500;
        public virtual int CpBombCooldownSeconds { get; set; } = 0;
        public virtual string CpBombRewardId { get; set; } = string.Empty;
        public virtual Color CpBombBackgroundColor { get; set; } = Color.white;

        public virtual bool CpFasterEnabled { get; set; } = false;
        public virtual int CpFasterCost { get; set; } = 500;
        public virtual int CpFasterCooldownSeconds { get; set; } = 0;
        public virtual string CpFasterRewardId { get; set; } = string.Empty;
        public virtual Color CpFasterBackgroundColor { get; set; } = Color.white;

        public virtual bool CpSuperFastEnabled { get; set; } = false;
        public virtual int CpSuperFastCost { get; set; } = 500;
        public virtual int CpSuperFastCooldownSeconds { get; set; } = 0;
        public virtual string CpSuperFastRewardId { get; set; } = string.Empty;
        public virtual Color CpSuperFastBackgroundColor { get; set; } = Color.white;

        public virtual bool CpSlowerEnabled { get; set; } = false;
        public virtual int CpSlowerCost { get; set; } = 500;
        public virtual int CpSlowerCooldownSeconds { get; set; } = 0;
        public virtual string CpSlowerRewardId { get; set; } = string.Empty;
        public virtual Color CpSlowerBackgroundColor { get; set; } = Color.white;

        public virtual bool CpFlashbangEnabled { get; set; } = false;
        public virtual int CpFlashbangCost { get; set; } = 500;
        public virtual int CpFlashbangCooldownSeconds { get; set; } = 0;
        public virtual string CpFlashbangRewardId { get; set; } = string.Empty;
        public virtual Color CpFlashbangBackgroundColor { get; set; } = Color.white;

        // --- PRD aliases ---
        public virtual bool ChannelPointsEnabled { get; set; } = false;

        [Ignore]
        public bool RainbowNotesEnabled { get => RainbowEnabled; set => RainbowEnabled = value; }

        [Ignore]
        public bool GhostNotesEnabled { get => GhostEnabled; set => GhostEnabled = value; }

        [Ignore]
        public bool DisappearingArrowsEnabled { get => DisappearEnabled; set => DisappearEnabled = value; }

        [Ignore]
        public bool BombsEnabled { get => BombEnabled; set => BombEnabled = value; }

        [Ignore]
        public bool SpeedChangeEnabled
        {
            get => FasterEnabled || SuperFastEnabled || SlowerEnabled;
            set
            {
                FasterEnabled = value;
                SuperFastEnabled = value;
                SlowerEnabled = value;
            }
        }

        [Ignore]
        public bool EndlessModeEnabled { get => PlayFirstSubmitLaterEnabled; set => PlayFirstSubmitLaterEnabled = value; }
        public virtual string RainbowNotePermission { get; set; } = "everyone";
        public virtual string GhostNotePermission { get; set; } = "everyone";
        public virtual int MaxCommandsPerSecond { get; set; } = 3;

        [Ignore]
        public int RainbowNotesCooldownSeconds { get => (int)RainbowCooldownSeconds; set => RainbowCooldownSeconds = value; }

        [Ignore]
        public int GhostNotesCooldownSeconds { get => (int)GhostCooldownSeconds; set => GhostCooldownSeconds = value; }

        public virtual void Changed()
        {
            _log.Debug("PluginConfig changed - BSIPA config store notification");
        }

        public virtual void CopyFrom(PluginConfig other)
        {
            if (other == null) return;

            AccessToken = other.AccessToken;
            RefreshToken = other.RefreshToken;
            ClientId = other.ClientId;
            TokenExpiryTicks = other.TokenExpiryTicks;
            ChannelUserId = other.ChannelUserId;
            CachedBroadcasterId = other.CachedBroadcasterId;
            CachedBroadcasterLogin = other.CachedBroadcasterLogin;
            CachedBotUserId = other.CachedBotUserId;
            CachedBotUserLogin = other.CachedBotUserLogin;
        }
    }
}
