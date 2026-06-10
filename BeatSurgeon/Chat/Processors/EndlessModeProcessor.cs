using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    /*
    // Endless mode disabled for release — restore class body and DI binding when ready.
    internal sealed class EndlessModeProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("EndlessModeProcessor");
        private readonly GameplayManager _gameplayManager;

        public EndlessModeProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!endless", "!endlessstop", "!stopendless" };

        public bool CanHandle(ChatContext ctx)
        {
            return true;
        }

        public async Task ExecuteAsync(ChatContext ctx, CancellationToken ct)
        {
            _log.Command(ctx.Username, ctx.Command, true);
            string command = (ctx.Command ?? string.Empty).Trim().ToLowerInvariant();

            if (command == "!endlessstop" || command == "!stopendless")
            {
                await _gameplayManager.StopEndlessModeAsync(ctx, ct).ConfigureAwait(false);
                return;
            }

            float minutes = 60f;
            string[] parts = (ctx.MessageText ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && float.TryParse(parts[1], out float parsed) && parsed > 0f)
            {
                minutes = parsed;
            }

            await _gameplayManager.ApplyEndlessModeAsync(minutes, ctx, ct).ConfigureAwait(false);
        }
    }
    */
}
