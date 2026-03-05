using BeatSurgeon.UI.Settings;
using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Installers
{
    internal sealed class BeatSurgeonMenuInstaller : Installer
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("MenuInstaller");

        public override void InstallBindings()
        {
            _log.Lifecycle("InstallBindings - registering Menu-scoped bindings");

            Container.BindInterfacesAndSelfTo<SurgeonGameplaySetupTabRegistrar>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<BeatSurgeonMenuButtonHost>()
                .AsSingle()
                .NonLazy();

            _log.Lifecycle("InstallBindings complete");
        }
    }
}
