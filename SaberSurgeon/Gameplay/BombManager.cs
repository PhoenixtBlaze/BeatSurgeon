using SaberSurgeon.Chat;
using System.Collections.Generic;
using UnityEngine;

namespace SaberSurgeon.Gameplay
{
    public class BombManager : MonoBehaviour
    {
        // Singleton instance + backing GameObject
        private static BombManager _instance;
        private static GameObject _go;

        /// <summary>True while a bomb is armed but not yet assigned to a note.</summary>
        public static bool BombArmed { get; private set; }

        /// <summary>Name of the viewer who armed the current bomb.</summary>
        public static string CurrentBomberName { get; private set; } = "Unknown";

        //time-based bomb window per command
        public static float BombWindowEndTime { get; private set; }
        public static bool BombConsumed { get; private set; }

        /// <summary>
        /// Tracks which NoteData instances are bombs and who armed them.
        /// Only notes you mark via BombNotePatch are stored here.
        /// </summary>
        private readonly Dictionary<NoteData, string> _bombNotes = new Dictionary<NoteData, string>();

        //is the bomb window currently active?
        public static bool IsBombWindowActive
        {
            get
            {
                if (!BombArmed) return false;
                if (BombConsumed) return false;
                if (Time.time > BombWindowEndTime) return false;
                return true;
            }
        }

        /// <summary>Global accessor for BombManager.</summary>
        public static BombManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("SaberSurgeon_BombManager_GO");
                    Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<BombManager>();
                    Plugin.Log.Info("BombManager: Created new instance");
                }

                return _instance;
            }
        }

        /// <summary>Called by CommandHandler when !bomb is used.</summary>
        public bool ArmBomb(string bomberName, float durationSeconds)
        {
            // Require being in a map (same pattern as other managers)
            var inMap = Resources.FindObjectsOfTypeAll<BeatmapObjectSpawnController>().Length > 0;
            if (!inMap)
            {
                Plugin.Log.Warn("BombManager: Not in a map (no BeatmapObjectSpawnController).");
                return false;
            }

            BombArmed = true;
            BombConsumed = false;
            BombWindowEndTime = Time.time + durationSeconds;

            // Clear any leftover bomb notes from previous commands
            _bombNotes.Clear();

            CurrentBomberName = string.IsNullOrEmpty(bomberName) ? "Unknown" : bomberName;
            Plugin.Log.Info($"BombManager: Bomb armed for user {CurrentBomberName}");

            //ChatManager.GetInstance().SendChatMessage($"Bomb armed! Watch out {CurrentBomberName}…");
       

            return true;
        }

        /// <summary>
        /// Called from BombNotePatch when a note spawns and a bomb is armed.
        /// Marks this NoteData as the active bomb and consumes the armed flag.
        /// </summary>
        public void MarkNoteAsBomb(NoteData noteData)
        {
            if (noteData == null)
                return;

            if (!_bombNotes.ContainsKey(noteData))
            {
                _bombNotes[noteData] = CurrentBomberName;
                Plugin.Log.Info(
                    $"BombManager: Marked note as bomb for {CurrentBomberName} at t={noteData.time:F3}");
            }
        }

        /// <summary>
        /// Called from BombCutPatch when any note is cut.
        /// Returns true only if this was one of our bombs and outputs the bomber name.
        /// Also sends the chat message.
        /// </summary>
        public bool TryConsumeBomb(NoteData noteData, out string bomber)
        {
            bomber = null;

            if (noteData == null)
                return false;

            if (!_bombNotes.TryGetValue(noteData, out bomber))
                return false; // not a bomb

            _bombNotes.Remove(noteData);

            // Mark this bomb session as completed
            BombConsumed = true;
            BombArmed = false;

            Plugin.Log.Info(
                $"BombManager: Bomb cut! Triggered by {bomber} at t={noteData.time:F3}");

            //ChatManager.GetInstance().SendChatMessage(
               // $"!BOOM! Bomb triggered by {bomber}!");

            return true;
        }

        public void StopAllBombWatchdogs()
        {
            var watchdogs = Resources.FindObjectsOfTypeAll<RendererWatchdog>();
            foreach (var wd in watchdogs)
            {
                if (wd != null)
                {
                    Destroy(wd);
                }
            }
            Plugin.Log.Info("BombManager: Stopped all RendererWatchdog instances after bomb cut");
        }


        /// <summary>Optional cleanup if you ever want to reset between sessions.</summary>
        public void Shutdown()
        {
            BombArmed = false;
            CurrentBomberName = "Unknown";
            _bombNotes.Clear();
        }
    }
}
