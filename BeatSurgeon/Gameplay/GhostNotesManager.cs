using System.Collections;
using BeatSurgeon.Chat;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Controls the timed Ghost Notes effect triggered by chat / UI.
    /// Patterned after RainbowManager.
    /// </summary>
    public class GhostNotesManager : MonoBehaviour
    {
        private static GhostNotesManager _instance;
        private static GameObject _go;
        private Coroutine _ghostCoroutine;

        /// <summary>
        /// True while ghost notes should be applied to newly spawned notes.
        /// Used by Harmony patches.
        /// </summary>
        public static bool GhostActive { get; private set; }
        public static bool FirstNoteShown { get; set; }


        public static GhostNotesManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_GhostNotesManager_GO");
                    Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<GhostNotesManager>();
                    LogUtils.Debug(() => "GhostNotesManager: Created new instance");
                }

                return _instance;
            }
        }

        /// <summary>
        /// Enable ghost notes for durationSeconds. Returns true if it could start.
        /// </summary>
        public bool StartGhost(float durationSeconds, string requesterName = null)
        {
            var inMap = Resources.FindObjectsOfTypeAll<BeatmapObjectSpawnController>().Length > 0;
            if (!inMap)
            {
                Plugin.Log.Warn("GhostNotesManager: Not in a map (no BeatmapObjectSpawnController).");
                return false;
            }

            // Reset first-note flag on each activation
            FirstNoteShown = false;

            if (_ghostCoroutine != null)
            {
                StopCoroutine(_ghostCoroutine);
                _ghostCoroutine = null;
            }

            MultiplayerStateClient.SetActiveCommand("ghost");
            _ghostCoroutine = StartCoroutine(GhostCoroutine(durationSeconds, requesterName));
            return true;
        }

        /// <summary>
        /// Immediately stop the ghost effect if running.
        /// </summary>
        public void StopGhost()
        {
            if (_ghostCoroutine != null)
            {
                StopCoroutine(_ghostCoroutine);
                _ghostCoroutine = null;
            }

            if (GhostActive)
            {
                GhostActive = false;
                MultiplayerStateClient.SetActiveCommand(null);
                LogUtils.Debug(() => "GhostNotesManager: Ghost notes manually stopped");
                //ChatManager.GetInstance().SendChatMessage("Ghost notes effect has been stopped.");
            }
        }

        private IEnumerator GhostCoroutine(float durationSeconds, string requesterName)
        {
            GhostActive = true;
            LogUtils.Debug(() => $"GhostNotesManager: Ghost notes enabled for {durationSeconds:F1}s");

            if (!string.IsNullOrEmpty(requesterName))
            {
                ChatManager.GetInstance().SendChatMessage(
                    $"!!Ghost notes enabled for {durationSeconds:F0} seconds! (requested by {requesterName})");
            }
            else
            {
                ChatManager.GetInstance().SendChatMessage(
                    $"!!Ghost notes enabled for {durationSeconds:F0} seconds!");
            }

            float elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            GhostActive = false;
            FirstNoteShown = false;
            _ghostCoroutine = null;

            LogUtils.Debug(() => "GhostNotesManager: Ghost notes finished");
            MultiplayerStateClient.SetActiveCommand(null);
            ChatManager.GetInstance().SendChatMessage("!!Ghost notes effect has ended.");
        }
    }
}
