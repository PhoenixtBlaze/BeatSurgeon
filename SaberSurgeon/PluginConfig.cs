using IPA.Config.Stores;

namespace SaberSurgeon
{
    // Must be public or internal with public virtual properties for BSIPA Generated<T>()
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; }

        // --- Commands / Toggles ---

        // Bomb command keyword (without leading '!')
        public virtual string BombCommandName { get; set; } = "bomb";

        // Command toggles
        public virtual bool RainbowEnabled { get; set; } = true;
        public virtual bool DisappearEnabled { get; set; } = true;
        public virtual bool GhostEnabled { get; set; } = true;
        public virtual bool BombEnabled { get; set; } = true;
        public virtual float BombTextHeight { get; set; } = 1.0f;     // vertical scale
        public virtual float BombTextWidth { get; set; } = 1.0f;      // horizontal scale
        public virtual float BombSpawnDistance { get; set; } = 10.0f; // units forward from player
        public virtual string BombFontType { get; set; } = "Default"; // dropdown selection
        public virtual bool FasterEnabled { get; set; } = false;
        public virtual bool SuperFastEnabled { get; set; } = false;
        public virtual bool SlowerEnabled { get; set; } = true;
        public virtual bool FlashbangEnabled { get; set; } = true;

        // Global + per‑command cooldowns
        public virtual bool GlobalCooldownEnabled { get; set; } = true;
        public virtual bool PerCommandCooldownsEnabled { get; set; } = false;
        public virtual float GlobalCooldownSeconds { get; set; } = 60f;

        public virtual float RainbowCooldownSeconds { get; set; } = 60f;
        public virtual float DisappearCooldownSeconds { get; set; } = 60f;
        public virtual float GhostCooldownSeconds { get; set; } = 60f;
        public virtual float BombCooldownSeconds { get; set; } = 60f;
        public virtual float FasterCooldownSeconds { get; set; } = 60f;
        public virtual float SuperFastCooldownSeconds { get; set; } = 60f;
        public virtual float SlowerCooldownSeconds { get; set; } = 60f;
        public virtual float FlashbangCooldownSeconds { get; set; } = 60f;

        // Only one speed effect at a time
        public virtual bool SpeedExclusiveEnabled { get; set; } = true;

        // --- OAuth Token Storage (Encrypted, used by TwitchAuthManager) ---

        public virtual string EncryptedAccessToken { get; set; } = "";
        public virtual string EncryptedRefreshToken { get; set; } = "";
        public virtual long TokenExpiryTicks { get; set; } = 0; // DateTime.Ticks

        // --- Twitch / Backend Settings ---

        // Cached info about the currently linked Twitch user (broadcaster)
        // Used by TwitchApiClient and TwitchEventClient
        public virtual string CachedBroadcasterId { get; set; } = "";
        public virtual string CachedBroadcasterLogin { get; set; } = "";

        // Supporter cache: subscription tier to phoenixblaze0
        public virtual int CachedSupporterTier { get; set; } = 0;

        // Backend selection and status
        public virtual bool PreferNativeTwitchBackend { get; set; } = true;   // Use your WebSocket backend first
        public virtual bool AllowChatPlexFallback { get; set; } = true;   // Fall back to ChatPlex if native fails
        public virtual string BackendStatus { get; set; } = "";     // For UI/debugging

        // WebSocket endpoint for your server (no channel_id here; added in TwitchEventClient)
        

        // Cached bot identity (for EventSub conditions like moderator_user_id / user_id)
        public virtual string CachedBotUserId { get; set; } = "";
        public virtual string CachedBotUserLogin { get; set; } = "";

        // Optional toggle so you can disable autoconnect from settings later
        public virtual bool AutoConnectTwitch { get; set; } = true;
        public virtual bool TwitchReauthRequired { get; set; } = false;



        // --- Endless / Song Requests ---

        /// Master toggle for chat song requests (!sr / !bsr).
        public virtual bool SongRequestsEnabled { get; set; } = true;

        /// Allow requesters to specify difficulty (e.g. "ex", "e+", "expert+", "hard").
        public virtual bool RequestAllowSpecificDifficulty { get; set; } = true;

        /// Allow requesters to specify time/range (e.g. "1:20" or "1:20-2:10").
        public virtual bool RequestAllowSpecificTime { get; set; } = true;

        /// Max number of pending requests in the queue. If exceeded, new requests are rejected.
        public virtual int QueueSizeLimit { get; set; } = 20;

        /// Prevent the same song being requested again too soon (simple “history window” size).
        /// 0 disables requeue blocking.
        public virtual int RequeueLimit { get; set; } = 10;

        /// Optional: allow old command name.
        public virtual bool BsrCommandAliasEnabled { get; set; } = true;


        public virtual bool PlayFirstSubmitLaterEnabled { get; set; } = true;
        public virtual bool ScoreSubmissionEnabled { get; set; } = true;
        public virtual bool AutoPauseOnMapEnd { get; set; } = true;

    }
}