using System;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using HarmonyLib;
using Zenject;

namespace BeatSurgeon.HarmonyPatches
{
    /*
    /// <summary>
    /// Binds <see cref="SeamlessTransitionController"/> into the GameCore Zenject container, but ONLY
    /// when an endless session is currently active. On any normal (non-endless) map this Postfix
    /// returns early, so the controller is never created and stock GameplayCore is completely
    /// unaffected. The bound GameObject lives and dies with the GameCore scene (correct lifecycle).
    /// </summary>
    [HarmonyPatch(typeof(GameplayCoreInstaller), "InstallBindings")]
    internal static class SeamlessInstallerPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SeamlessInstaller");

        [HarmonyPostfix]
        private static void Postfix(GameplayCoreInstaller __instance)
        {
            try
            {
                var cfg = PluginConfig.Instance;
                if (cfg == null || !cfg.SeamlessTransitionEnabled) return;

                var gm = GameplayManager.GetInstance();
                if (gm == null || !gm.IsPlaying()) return; // ENDLESS-ONLY hard gate

                var containerProp = AccessTools.Property(__instance.GetType(), "Container");
                if (!(containerProp?.GetValue(__instance) is DiContainer container))
                {
                    _log.Warn("Could not access installer Container; skipping seamless bind.");
                    return;
                }

                container.Bind<SeamlessTransitionController>()
                    .FromNewComponentOnNewGameObject()
                    .AsSingle()
                    .NonLazy();

                _log.Info("SeamlessTransitionController bound into GameCore (endless session active).");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "SeamlessInstallerPatch.Postfix");
            }
        }
    }
    */
}
