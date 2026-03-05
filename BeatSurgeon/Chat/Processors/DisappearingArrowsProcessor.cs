using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class DisappearingArrowsProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("DisappearingArrowsProcessor");
        private readonly GameplayManager _gameplayManager;

        public DisappearingArrowsProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!disappear", "!disappearingarrows" };

        public bool CanHandle(ChatContext ctx)
        {
            if (!_gameplayManager.IsInMap)
            {
                _log.Command(ctx.Username, ctx.Command, false, "NotInMap");
                return false;
            }

            return true;
        }

        public async Task ExecuteAsync(ChatContext ctx, CancellationToken ct)
        {
            _log.Command(ctx.Username, ctx.Command, true);
            await _gameplayManager.ApplyDisappearingArrowsAsync(ctx, ct).ConfigureAwait(false);
        }
    }
}
