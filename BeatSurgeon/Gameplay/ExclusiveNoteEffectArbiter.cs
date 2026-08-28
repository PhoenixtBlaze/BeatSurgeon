using System.Collections.Generic;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Main-thread exclusive claimer for glitter / subscriber trail / raid notes.
    /// Round-robins among kinds that both have pending work and can accept the note.
    /// </summary>
    internal enum ExclusiveNoteEffectKind
    {
        Glitter = 0,
        SubTrail = 1,
        Raid = 2
    }

    internal static class ExclusiveNoteEffectArbiter
    {
        private const int KindCount = 3;

        private static int _rrIndex;
        private static readonly Dictionary<GameNoteController, ExclusiveNoteEffectKind> _reservations =
            new Dictionary<GameNoteController, ExclusiveNoteEffectKind>(64);

        /// <summary>
        /// Attempts to reserve <paramref name="note"/> for <paramref name="kind"/>.
        /// Returns true only when the round-robin choice for this note is exactly that kind.
        /// Does not consume manager queues.
        /// </summary>
        internal static bool TryReserve(GameNoteController note, ExclusiveNoteEffectKind kind)
        {
            if (note == null)
            {
                return false;
            }

            if (_reservations.TryGetValue(note, out ExclusiveNoteEffectKind existing))
            {
                return existing == kind;
            }

            if (IsOwnedByAnyExclusiveManager(note))
            {
                return false;
            }

            // Stack flags: eligible[i] means kind i has pending work AND can accept this note.
            bool glitterOk = false;
            bool subTrailOk = false;
            bool raidOk = false;

            GlitterManager glitter = GlitterManager.Instance;
            if (glitter.HasPendingEffects && glitter.CanMarkNextEffect(note, note.noteData))
            {
                glitterOk = true;
            }

            SubscriberTrailCubeManager trail = SubscriberTrailCubeManager.Instance;
            if (trail.HasPendingNotes && trail.CanMarkNextNote(note))
            {
                subTrailOk = true;
            }

            RaidFountainNoteManager raid = RaidFountainNoteManager.Instance;
            if (raid.HasPendingNotes && raid.CanMarkNextNote(note))
            {
                raidOk = true;
            }

            bool requestedOk =
                kind == ExclusiveNoteEffectKind.Glitter ? glitterOk
                : kind == ExclusiveNoteEffectKind.SubTrail ? subTrailOk
                : raidOk;

            if (!requestedOk)
            {
                return false;
            }

            ExclusiveNoteEffectKind chosen = PickRoundRobin(glitterOk, subTrailOk, raidOk);
            if (chosen != kind)
            {
                return false;
            }

            _reservations[note] = kind;
            _rrIndex = ((int)kind + 1) % KindCount;
            return true;
        }

        internal static void Release(GameNoteController note)
        {
            if (note == null)
            {
                return;
            }

            _reservations.Remove(note);
        }

        internal static void Clear()
        {
            _reservations.Clear();
            _rrIndex = 0;
        }

        private static bool IsOwnedByAnyExclusiveManager(GameNoteController note)
        {
            return GlitterManager.Instance.IsNoteMarked(note)
                || SubscriberTrailCubeManager.Instance.IsNoteMarked(note)
                || RaidFountainNoteManager.Instance.IsNoteMarked(note);
        }

        private static ExclusiveNoteEffectKind PickRoundRobin(bool glitterOk, bool subTrailOk, bool raidOk)
        {
            for (int step = 0; step < KindCount; step++)
            {
                int index = (_rrIndex + step) % KindCount;
                if (index == (int)ExclusiveNoteEffectKind.Glitter && glitterOk)
                {
                    return ExclusiveNoteEffectKind.Glitter;
                }

                if (index == (int)ExclusiveNoteEffectKind.SubTrail && subTrailOk)
                {
                    return ExclusiveNoteEffectKind.SubTrail;
                }

                if (index == (int)ExclusiveNoteEffectKind.Raid && raidOk)
                {
                    return ExclusiveNoteEffectKind.Raid;
                }
            }

            // Caller already verified requested kind is eligible; fall back safely.
            if (glitterOk)
            {
                return ExclusiveNoteEffectKind.Glitter;
            }

            if (subTrailOk)
            {
                return ExclusiveNoteEffectKind.SubTrail;
            }

            return ExclusiveNoteEffectKind.Raid;
        }
    }
}
