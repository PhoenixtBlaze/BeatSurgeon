using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Chat.Processors
{
    internal sealed class RaidProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("RaidProcessor");
        private readonly GameplayManager _gameplayManager;

        public RaidProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!raid" };

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
            int requestedNotes = NumericBitCommandParser.ParseRequestedBits(ctx?.MessageText, "!raid");
            int clampedNotes = RaidFountainNoteManager.ClampNoteCount(requestedNotes);
            await EnsureAccessAsync(ctx, ct).ConfigureAwait(false);

            string displayName = string.IsNullOrWhiteSpace(ctx?.Username) ? "Someone" : ctx.Username.Trim();
            _log.Command(ctx.Username, ctx.Command, true, "notes=" + clampedNotes + " displayName=" + displayName);
            await _gameplayManager.ApplyRaidEffectAsync(ctx, displayName, clampedNotes, ct).ConfigureAwait(false);
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
                await RaidEffectAccessController.EnsureAutomaticEffectAuthorizedAsync(ct).ConfigureAwait(false);
                return;
            }

            await RaidEffectAccessController.EnsureAuthorizedAsync(ct).ConfigureAwait(false);
        }
    }
}
