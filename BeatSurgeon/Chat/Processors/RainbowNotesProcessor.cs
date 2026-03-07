using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class RainbowNotesProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("RainbowNotesProcessor");
        private readonly GameplayManager _gameplayManager;

        public RainbowNotesProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!rainbow", "!rainbownotes" };

        public bool CanHandle(ChatContext ctx)
        {
            if (!_gameplayManager.IsInMap)
            {
                _log.Command(ctx.Username, ctx.Command, false, "NotInMap");
                return false;
            }

            if (!ctx.HasPermission(PluginConfig.Instance.RainbowNotePermission))
            {
                _log.Command(ctx.Username, ctx.Command, false, "InsufficientPermission");
                return false;
            }

            return true;
        }

        public async Task ExecuteAsync(ChatContext ctx, CancellationToken ct)
        {
            _log.Command(ctx.Username, ctx.Command, true);
            await _gameplayManager.ApplyRainbowNotesAsync(ctx, ct).ConfigureAwait(false);
        }
    }
}
