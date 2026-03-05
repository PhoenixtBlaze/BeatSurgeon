using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class GhostNotesProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("GhostNotesProcessor");
        private readonly GameplayManager _gameplayManager;

        public GhostNotesProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!ghost", "!ghostnotes" };

        public bool CanHandle(ChatContext ctx)
        {
            if (!_gameplayManager.IsInMap)
            {
                _log.Command(ctx.Username, ctx.Command, false, "NotInMap");
                return false;
            }

            if (!ctx.HasPermission(PluginConfig.Instance.GhostNotePermission))
            {
                _log.Command(ctx.Username, ctx.Command, false, "InsufficientPermission");
                return false;
            }

            return true;
        }

        public async Task ExecuteAsync(ChatContext ctx, CancellationToken ct)
        {
            _log.Command(ctx.Username, ctx.Command, true);
            await _gameplayManager.ApplyGhostNotesAsync(ctx, ct).ConfigureAwait(false);
        }
    }
}
