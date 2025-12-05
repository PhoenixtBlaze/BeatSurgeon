using IPA.Config.Stores;

namespace SaberSurgeon
{
    // Must be public or internal with public virtual properties for BSIPA Generated<T>()
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; }

        // Command toggles
        public virtual bool RainbowEnabled { get; set; } = true;
        public virtual bool DisappearEnabled { get; set; } = true;
        public virtual bool GhostEnabled { get; set; } = true;
        public virtual bool BombEnabled { get; set; } = true;
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

        public virtual bool SpeedExclusiveEnabled { get; set; } = true;
    }
}
