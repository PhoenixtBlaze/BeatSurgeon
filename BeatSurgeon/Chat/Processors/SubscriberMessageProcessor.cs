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

        public string[] HandledCommands => new[] { "!smsg", "!subcubes" };

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
            bool isSubCubes = string.Equals(ctx?.Command, "!subcubes", StringComparison.OrdinalIgnoreCase);
            bool isMultiplayerSync = ctx?.TriggerSource == TriggerSource.MultiplayerSync;

            if (isSubCubes)
            {
                int trailCubeCount = ParseSubCubesCount(ctx?.MessageText);
                await EnsureAccessAsync(ctx, ct).ConfigureAwait(false);

                _log.Command(ctx.Username, ctx.Command, true, "trailCubeCount=" + trailCubeCount + " multiplayerSync=" + isMultiplayerSync);
                // Cubes only — empty display text; ApplySubscriberMessageAsync skips canvas on MultiplayerSync.
                await _gameplayManager.ApplySubscriberMessageAsync(
                    ctx,
                    displayText: string.Empty,
                    ct,
                    trailCubeCount).ConfigureAwait(false);
                return;
            }

            string displayText = ExtractMessageSuffix(ctx?.MessageText, out int smsgTrailCount);
            await EnsureAccessAsync(ctx, ct).ConfigureAwait(false);

            _log.Command(
                ctx.Username,
                ctx.Command,
                true,
                "displayText=" + displayText + " trailCubeCount=" + smsgTrailCount + " multiplayerSync=" + isMultiplayerSync);

            // Legacy multiplayer !smsg payloads: still cubes-only on clients (no canvas text).
            await _gameplayManager.ApplySubscriberMessageAsync(ctx, displayText, ct, smsgTrailCount).ConfigureAwait(false);
        }

        private static async Task EnsureAccessAsync(ChatContext ctx, CancellationToken ct)
        {
            if (ctx?.TriggerSource == TriggerSource.MultiplayerSync)
            {
                return;
            }

            if (ctx?.TriggerSource == TriggerSource.AutomaticEvent
                || ctx?.TriggerSource == TriggerSource.BitEvent)
            {
                await SubscriberEffectAccessController.EnsureAutomaticEffectAuthorizedAsync(ct).ConfigureAwait(false);
                return;
            }

            await SubscriberEffectAccessController.EnsureAuthorizedAsync(ct).ConfigureAwait(false);
        }

        private static int ParseSubCubesCount(string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                return 5;
            }

            if (!ChatContext.TryExtractFirstCommandToken(messageText, out _, out int commandStart, out int commandLength))
            {
                return 5;
            }

            int suffixStart = commandStart + commandLength;
            if (suffixStart >= messageText.Length)
            {
                return 5;
            }

            string raw = messageText.Substring(suffixStart).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 5;
            }

            int space = raw.IndexOf(' ');
            string token = space > 0 ? raw.Substring(0, space) : raw;
            if (int.TryParse(token, out int count) && count >= 0)
            {
                return count;
            }

            return 5;
        }

        private static string ExtractMessageSuffix(string messageText, out int trailCubeCount)
        {
            trailCubeCount = 5;
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

            // Optional multiplayer/host encoding: "!smsg 8 Alice subscribed!"
            int firstSpace = raw.IndexOf(' ');
            if (firstSpace > 0
                && int.TryParse(raw.Substring(0, firstSpace), out int parsedTrail)
                && parsedTrail >= 0)
            {
                trailCubeCount = parsedTrail;
                raw = raw.Substring(firstSpace + 1).Trim();
            }

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
