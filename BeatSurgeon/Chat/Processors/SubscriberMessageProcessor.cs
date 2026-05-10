using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class SubscriberMessageProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SubscriberMessageProcessor");
        private const int MaxSubscriberMessageLength = 100;
        private readonly GameplayManager _gameplayManager;

        public SubscriberMessageProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!smsg" };

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
            if (ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                throw new InvalidOperationException("!smsg is only available for subscribers.");
            }

            string displayText = ExtractMessageSuffix(ctx?.MessageText);
            await SubscriberEffectAccessController.EnsureAuthorizedAsync(ct).ConfigureAwait(false);
            _log.Command(ctx.Username, ctx.Command, true, "displayText=" + displayText);
            await _gameplayManager.ApplySubscriberMessageAsync(ctx, displayText, ct).ConfigureAwait(false);
        }

        private static string ExtractMessageSuffix(string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                throw new InvalidOperationException("Usage: !smsg <message>");
            }

            if (!ChatContext.TryExtractFirstCommandToken(messageText, out _, out int commandStart, out int commandLength))
            {
                throw new InvalidOperationException("Usage: !smsg <message>");
            }

            int suffixStart = commandStart + commandLength;
            if (suffixStart >= messageText.Length)
            {
                throw new InvalidOperationException("Usage: !smsg <message>");
            }

            string raw = messageText.Substring(suffixStart).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("Usage: !smsg <message>");
            }

            if (raw.Length > MaxSubscriberMessageLength)
            {
                raw = raw.Substring(0, MaxSubscriberMessageLength).TrimEnd();
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("Usage: !smsg <message>");
            }

            return raw;
        }
    }
}
