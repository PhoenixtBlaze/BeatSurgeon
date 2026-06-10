using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    /*
    // Song request commands disabled for release (Endless mode) — restore class body and DI binding when ready.
    internal sealed class SongRequestProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SongRequestProcessor");
        private readonly GameplayManager _gameplayManager;

        public SongRequestProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!sr", "!bsr" };

        public bool CanHandle(ChatContext ctx)
        {
            if (!PluginConfig.Instance.SongRequestsEnabled)
            {
                _log.Command(ctx.Username, ctx.Command, false, "SongRequestsDisabled");
                return false;
            }

            return true;
        }

        public async Task ExecuteAsync(ChatContext ctx, CancellationToken ct)
        {
            _log.Command(ctx.Username, ctx.Command, true);
            string[] parts = (ctx.MessageText ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                //ChatManager.GetInstance()?.SendMutedChatMessage("Song request usage is !sr <bsrCode>.");
                _log.Command(ctx.Username, ctx.Command, false, "MissingBsrCode");
                return;
            }

            string bsrCode = parts[1].Trim();
            await _gameplayManager.ApplySongRequestAsync(bsrCode, ctx, ct).ConfigureAwait(false);
        }
    }
    */
}
