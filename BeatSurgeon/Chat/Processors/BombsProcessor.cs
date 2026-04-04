using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class BombsProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("BombsProcessor");
        private const int MaxBombMessageLength = 70;
        private readonly GameplayManager _gameplayManager;

        public BombsProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!bomb", "!bmsg" };

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
            string displayTextOverride = GetDisplayTextOverride(ctx);

            if (string.IsNullOrWhiteSpace(displayTextOverride))
            {
                _log.Command(ctx.Username, ctx.Command, true);
            }
            else
            {
                _log.Command(ctx.Username, ctx.Command, true, "displayText=" + displayTextOverride);
            }

            await _gameplayManager.ApplyBombAsync(ctx, ct, displayTextOverride).ConfigureAwait(false);
        }

        private static string GetDisplayTextOverride(ChatContext ctx)
        {
            if (!string.Equals(ctx?.Command, "!bmsg", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ExtractMessageSuffix(ctx?.MessageText);
        }

        private static string ExtractMessageSuffix(string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                return null;
            }

            if (!ChatContext.TryExtractFirstCommandToken(messageText, out _, out int commandStart, out int commandLength))
            {
                return null;
            }

            int suffixStart = commandStart + commandLength;
            if (suffixStart >= messageText.Length)
            {
                return null;
            }

            string raw = messageText.Substring(suffixStart).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (raw.Length > MaxBombMessageLength)
            {
                raw = raw.Substring(0, MaxBombMessageLength).TrimEnd();
            }

            return raw;
        }
    }
}
