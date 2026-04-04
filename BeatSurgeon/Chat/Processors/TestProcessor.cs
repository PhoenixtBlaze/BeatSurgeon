using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class TestProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TestProcessor");
        private readonly GameplayManager _gameplayManager;

        public TestProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!test" };

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
            await _gameplayManager.ApplyOutlineEffectAsync(ctx, ct).ConfigureAwait(false);
        }
    }
}
