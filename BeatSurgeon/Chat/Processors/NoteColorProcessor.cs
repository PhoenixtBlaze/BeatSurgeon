using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using UnityEngine;

namespace BeatSurgeon.Chat.Processors
{
    /// <summary>
    /// Handles !notecolor / !notecolour commands.
    /// Parses two color arguments (named or hex) from the chat message and applies
    /// fixed left/right note colors via RainbowManager.StartNoteColor.
    ///
    /// Uses the same RainbowNotePermission, RainbowEnabled toggle, and RainbowEffectSeconds
    /// as the rainbow command since notecolor is a derivative of that system.
    ///
    /// Usage: !notecolor &lt;leftColor&gt; &lt;rightColor&gt;
    ///   Named:  !notecolor pink hotpink
    ///   Spaced: !notecolor light blue sky blue
    ///   Hex:    !notecolor #FF0000 #0000FF
    ///   Mixed:  !notecolor papayawhip #0000FF
    /// </summary>
    internal sealed class NoteColorProcessor : ICommandProcessor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("NoteColorProcessor");
        private readonly GameplayManager _gameplayManager;

        public NoteColorProcessor(GameplayManager gameplayManager)
        {
            _gameplayManager = gameplayManager;
        }

        public string[] HandledCommands => new[] { "!notecolor", "!notecolour" };

        public bool CanHandle(ChatContext ctx)
        {
            if (!_gameplayManager.IsInMap)
            {
                _log.Command(ctx.Username, ctx.Command, false, "NotInMap");
                return false;
            }

            if (ctx?.TriggerSource != TriggerSource.MultiplayerSync
                && !ctx.HasPermission(PluginConfig.Instance.RainbowNotePermission))
            {
                _log.Command(ctx.Username, ctx.Command, false, "InsufficientPermission");
                return false;
            }

            return true;
        }

        public async Task ExecuteAsync(ChatContext ctx, CancellationToken ct)
        {
            _log.Command(ctx.Username, ctx.Command, true);

            string[] tokens = (ctx.MessageText ?? string.Empty)
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            Color left = Color.white;
            Color right = Color.black;
            int parsed = 0;

            // Skip command token at [0]; greedily consume named/hex colors
            // (supports multi-word names like "light blue", "medium sea green").
            for (int i = 1; i < tokens.Length && parsed < 2;)
            {
                if (NamedColorParser.TryParseFromTokens(tokens, i, out Color c, out int consumed))
                {
                    if (parsed == 0) left = c;
                    else right = c;
                    parsed++;
                    i += consumed;
                }
                else
                {
                    i++;
                }
            }

            if (parsed < 2)
                throw new InvalidOperationException(
                    "Usage: !notecolor <leftColor> <rightColor>  " +
                    "e.g. !notecolor pink blue  or  !notecolor light blue #0000FF");

            await _gameplayManager.ApplyNoteColorAsync(ctx, left, right, ct).ConfigureAwait(false);
        }
    }
}
