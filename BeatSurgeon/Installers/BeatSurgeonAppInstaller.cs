using BeatSurgeon.Chat;
using BeatSurgeon.Chat.Processors;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Integration;
using BeatSurgeon.Integrations;
using BeatSurgeon;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Installers
{
    internal sealed class BeatSurgeonAppInstaller : Installer
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("AppInstaller");

        public override void InstallBindings()
        {
            _log.Lifecycle("InstallBindings - registering App-scoped singletons");

            // Legacy singleton bridge for existing gameplay call sites.
            Container.Bind<GameplayManager>()
                .FromMethod(_ => GameplayManager.GetInstance())
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<DeferredEventQueue>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<AccSaberClient>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<BombFontPreloadService>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<TwitchAuthManager>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<PatreonAuthManager>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<TwitchApiClient>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<PatreonApiClient>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<TwitchChannelPointsManager>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<TwitchEventSubClient>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<FollowEventCoordinator>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<SubscriberEventCoordinator>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<ChannelPointCommandExecutor>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<ChannelPointRouter>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<ChatManager>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<CommandHandler>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<MultiplayerRoomSyncClient>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<AutomaticEffectDedupService>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<IntegrationCommandExecutor>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<IntegrationEventExecutor>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<IntegrationApiCoordinator>()
                .AsSingle()
                .NonLazy();

            Container.BindInterfacesAndSelfTo<RankedMapDetectionService>()
                .AsSingle()
                .NonLazy();

            Container.Bind<ICommandProcessor>().To<RainbowNotesProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<NoteColorProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<GhostNotesProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<DisappearingArrowsProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<BombsProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<SpeedChangeProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<FlashbangProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<FollowerMessageProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<SubscriberMessageProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<GlitterProcessor>().AsSingle();
            Container.Bind<ICommandProcessor>().To<TestProcessor>().AsSingle();
            // Endless mode disabled for release — restore bindings when ready.
            // Container.Bind<ICommandProcessor>().To<EndlessModeProcessor>().AsSingle();
            // Container.Bind<ICommandProcessor>().To<SongRequestProcessor>().AsSingle();

            _log.Info("Registered 11 ICommandProcessor implementations");
            _log.Lifecycle("InstallBindings complete");
        }
    }
}
