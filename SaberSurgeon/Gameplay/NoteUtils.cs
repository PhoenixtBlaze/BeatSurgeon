using UnityEngine;

namespace SaberSurgeon.Gameplay
{
    internal static class NoteUtils
    {
        internal static NoteControllerBase FindNoteControllerParent(Component visualsComponent)
        {
            return visualsComponent?.GetComponentInParent<NoteControllerBase>();
        }
    }
}
