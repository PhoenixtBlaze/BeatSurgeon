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
    ///   Named:  !notecolor red blue
    ///   Hex:    !notecolor #FF0000 #0000FF
    ///   Mixed:  !notecolor red #0000FF
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

            if (!ctx.HasPermission(PluginConfig.Instance.RainbowNotePermission))
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

            for (int i = 1; i < tokens.Length && parsed < 2; i++)
            {
                string token = tokens[i];
                // Try the token as-is first (handles named colors like "red", "blue",
                // and already-prefixed hex like "#FF0000").
                // If that fails, prepend # to handle bare hex input like "FF0000".
                bool ok = ColorUtility.TryParseHtmlString(token, out Color c);
                if (!ok) ok = ColorUtility.TryParseHtmlString("#" + token, out c);

                if (ok)
                {
                    if (parsed == 0) left = c;
                    else right = c;
                    parsed++;
                }
            }

            if (parsed < 2)
                throw new InvalidOperationException(
                    "Usage: !notecolor <leftColor> <rightColor>  " +
                    "e.g. !notecolor red blue  or  !notecolor #FF0000 #0000FF");

            await _gameplayManager.ApplyNoteColorAsync(ctx, left, right, ct).ConfigureAwait(false);
        }
    }
}
