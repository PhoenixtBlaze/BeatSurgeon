using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Gameplay
{
    internal sealed class BombFontPreloadService : IInitializable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("BombFontPreload");

        public void Initialize()
        {
            _log.Lifecycle("Initialize - scheduling bomb font preload");
            FontBundleLoader.StartPreload();
        }
    }
}