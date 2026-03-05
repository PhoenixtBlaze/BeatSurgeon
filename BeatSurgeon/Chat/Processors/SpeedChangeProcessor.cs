using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class SpeedChangeProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SpeedChangeProcessor");
        private readonly GameplayManager _gameplayManager;

        public SpeedChangeProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!faster", "!superfast", "!slower" };

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
            string effectKey = ctx.Command.TrimStart('!').ToLowerInvariant();
            _log.Command(ctx.Username, ctx.Command, true);
            await _gameplayManager.ApplySpeedAsync(effectKey, ctx, ct).ConfigureAwait(false);
        }
    }
}
