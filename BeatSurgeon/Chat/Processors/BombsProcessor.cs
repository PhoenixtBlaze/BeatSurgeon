using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class BombsProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("BombsProcessor");
        private readonly GameplayManager _gameplayManager;

        public BombsProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!bomb" };

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
            await _gameplayManager.ApplyBombAsync(ctx, ct).ConfigureAwait(false);
        }
    }
}
