using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class GlitterProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("GlitterProcessor");
        private readonly GameplayManager _gameplayManager;

        public GlitterProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!glitter" };

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
            int requestedBits = NumericBitCommandParser.ParseRequestedBits(ctx?.MessageText, "!glitter");
            await BitEffectAccessController.EnsureAuthorizedAsync(ct).ConfigureAwait(false);
            _log.Command(
                ctx.Username,
                ctx.Command,
                true,
                "bits=" + requestedBits + " source=" + (ctx != null ? ctx.TriggerSource.ToString() : TriggerSource.Chat.ToString()));
            await _gameplayManager.ApplyGlitterAsync(ctx, requestedBits, ct).ConfigureAwait(false);
        }
    }
}