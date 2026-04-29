using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class FollowerMessageProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("FollowerMessageProcessor");
        private const int MaxFollowerMessageLength = 100;
        private readonly GameplayManager _gameplayManager;

        public FollowerMessageProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!fmsg" };

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
            string displayText = ExtractMessageSuffix(ctx?.MessageText);
            await FollowEffectAccessController.EnsureAuthorizedAsync(ct).ConfigureAwait(false);
            _log.Command(ctx.Username, ctx.Command, true, "displayText=" + displayText);
            await _gameplayManager.ApplyFollowerMessageAsync(ctx, displayText, ct).ConfigureAwait(false);
        }

        private static string ExtractMessageSuffix(string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                throw new InvalidOperationException("Usage: !fmsg <message>");
            }

            if (!ChatContext.TryExtractFirstCommandToken(messageText, out _, out int commandStart, out int commandLength))
            {
                throw new InvalidOperationException("Usage: !fmsg <message>");
            }

            int suffixStart = commandStart + commandLength;
            if (suffixStart >= messageText.Length)
            {
                throw new InvalidOperationException("Usage: !fmsg <message>");
            }

            string raw = messageText.Substring(suffixStart).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("Usage: !fmsg <message>");
            }

            if (raw.Length > MaxFollowerMessageLength)
            {
                raw = raw.Substring(0, MaxFollowerMessageLength).TrimEnd();
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("Usage: !fmsg <message>");
            }

            return raw;
        }
    }
}