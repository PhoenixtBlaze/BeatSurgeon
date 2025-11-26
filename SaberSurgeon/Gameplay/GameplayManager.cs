using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SongCore;
using SongCore.Utilities;
using Zenject;


namespace SaberSurgeon.Gameplay
{
    /// <summary>
    /// Manages the endless mode gameplay loop:
    /// - Timer countdown
    /// - Random song selection and playback
    /// - Song request queue from chat
    /// - Smooth transitions between songs
    /// </summary>
    public class GameplayManager : MonoBehaviour
    {
        private static GameplayManager _instance;
        private static GameObject _persistentGO;

        // Dependencies
        private MenuTransitionsHelper _menuTransitionsHelper;
        [Inject] private BeatmapLevelsModel _beatmapLevelsModel;
        private EnvironmentsListModel _environmentsListModel;
        private GameplayModifiers _capturedModifiers;
        private PlayerSpecificSettings _capturedPlayerSettings;
        private ColorScheme _capturedColorScheme;

        // State
        private bool _isPlaying = false;
        private float _remainingTime = 0f;
        private float _totalTime = 0f;
        private List<BeatmapLevel> _availableLevels = new List<BeatmapLevel>();
        private List<string> _playedLevelIds = new List<string>();
        private Queue<SongRequest> _requestQueue = new Queue<SongRequest>();
        private bool _isLoadingSong = false;
        // A reasonable “default with No Fail on” set of modifiers
        private static GameplayModifiers CreateDefaultNoFailModifiers()
        {
            return new GameplayModifiers(
                GameplayModifiers.EnergyType.Bar,          // energyType
                true,                                      // noFailOn0Energy
                false,                                     // instaFail
                false,                                     // failOnSaberClash
                GameplayModifiers.EnabledObstacleType.All, // enabledObstacleType
                false,                                     // noBombs
                false,                                     // noWalls
                false,                                     // noArrows
                false,                                     // ghostNotes
                GameplayModifiers.SongSpeed.Normal,        // songSpeed
                false,                                     // disappearingArrows
                false,                                     // strictAngles
                false,                                     // proMode
                false,                                     // smallCubes
                false                                      // zenMode (or last bool in your version)
            );
        }

        // Coroutines
        private Coroutine _gameplayCoroutine;
        private Coroutine _timerCoroutine;

        public static GameplayManager GetInstance()
        {
            if (_instance == null)
            {
                _persistentGO = new GameObject("SaberSurgeon_GameplayManager_GO");
                DontDestroyOnLoad(_persistentGO);
                _instance = _persistentGO.AddComponent<GameplayManager>();
                Plugin.Log.Info("GameplayManager: Created new instance");
            }
            return _instance;
        }

        /// <summary>
        /// Start the endless mode with specified duration in minutes
        /// </summary>
        public void StartEndlessMode(float durationMinutes)
        {
            if (_isPlaying)
            {
                Plugin.Log.Warn("GameplayManager: Already playing!");
                return;
            }


            // NEW: make sure MenuTransitionsHelper exists
            if (!EnsureDependencies())
            {
                Plugin.Log.Error("GameplayManager: Cannot start – MenuTransitionsHelper missing");
                return;
            }


            Plugin.Log.Info($"GameplayManager: Starting Endless Mode for {durationMinutes} minutes");

            CapturePlayerSettings();
            _totalTime = durationMinutes * 60f; // Convert to seconds
            _remainingTime = _totalTime;
            _isPlaying = true;
            _playedLevelIds.Clear();
            _requestQueue.Clear();

            // Load available custom songs
            LoadAvailableSongs();

            if (_availableLevels.Count == 0)
            {
                Plugin.Log.Error("GameplayManager: No custom songs found!");
                StopEndlessMode();
                return;
            }

            Plugin.Log.Info($"GameplayManager: Found {_availableLevels.Count} custom songs");

            // Start gameplay loop
            _gameplayCoroutine = StartCoroutine(GameplayLoop());
            _timerCoroutine = StartCoroutine(TimerCountdown());
        }

        /// <summary>
        /// Stop the endless mode
        /// </summary>
        public void StopEndlessMode()
        {
            Plugin.Log.Info("GameplayManager: Stopping Endless Mode");
            _isPlaying = false;

            if (_gameplayCoroutine != null)
            {
                StopCoroutine(_gameplayCoroutine);
                _gameplayCoroutine = null;
            }

            if (_timerCoroutine != null)
            {
                StopCoroutine(_timerCoroutine);
                _timerCoroutine = null;
            }

            _playedLevelIds.Clear();
            _requestQueue.Clear();
        }

        /// <summary>
        /// Queue a song request from chat (BSR code)
        /// </summary>
        public void QueueSongRequest(string bsrCode, string requesterName)
        {
            if (!_isPlaying)
            {
                Plugin.Log.Warn("GameplayManager: Not in gameplay mode, ignoring request");
                return;
            }

            Plugin.Log.Info($"GameplayManager: Queueing song request {bsrCode} from {requesterName}");

            var request = new SongRequest
            {
                BsrCode = bsrCode,
                RequesterName = requesterName,
                RequestTime = DateTime.UtcNow
            };

            _requestQueue.Enqueue(request);
        }

        public void SetDependencies(MenuTransitionsHelper menuTransitionsHelper,
                            EnvironmentsListModel environmentsListModel)
        {
            _menuTransitionsHelper = menuTransitionsHelper;
            _environmentsListModel = environmentsListModel;

            Plugin.Log.Info("GameplayManager: Dependencies injected");
        }

        /// <summary>
        /// Get remaining time in seconds
        /// </summary>
        public float GetRemainingTime() => _remainingTime;

        /// <summary>
        /// Get total time in seconds
        /// </summary>
        public float GetTotalTime() => _totalTime;

        /// <summary>
        /// Check if currently playing
        /// </summary>
        public bool IsPlaying() => _isPlaying;

        /// <summary>
        /// Load all available custom songs from SongCore
        /// </summary>
        private void LoadAvailableSongs()
        {
            Plugin.Log.Info("GameplayManager: Loading available songs from SongCore...");
            _availableLevels.Clear();

            try
            {
                // Get all custom levels from SongCore
                var customLevels = Loader.CustomLevels;

                if (customLevels == null || customLevels.Count == 0)
                {
                    Plugin.Log.Warn("GameplayManager: No custom levels found in SongCore");
                    return;
                }

                foreach (var kvp in customLevels)
                {
                    var level = kvp.Value;
                    if (level != null)
                    {
                        _availableLevels.Add(level);
                    }
                }

                Plugin.Log.Info($"GameplayManager: Loaded {_availableLevels.Count} custom songs");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"GameplayManager: Error loading songs: {ex.Message}");
            }
        }

        /// <summary>
        /// Main gameplay loop - handles song transitions
        /// </summary>
        private IEnumerator GameplayLoop()
        {
            while (_isPlaying && _remainingTime > 0)
            {
                // Check if there's a pending request
                BeatmapLevel nextLevel = null;
                string requestInfo = null;

                if (_requestQueue.Count > 0 && !_isLoadingSong)
                {
                    var request = _requestQueue.Dequeue();
                    Plugin.Log.Info($"GameplayManager: Processing request {request.BsrCode}");

                    // Try to find the song in loaded levels
                    nextLevel = FindLevelByBsr(request.BsrCode);

                    if (nextLevel != null)
                    {
                        requestInfo = $"Now playing request from {request.RequesterName}";
                    }
                    else
                    {
                        Plugin.Log.Warn($"GameplayManager: Requested song {request.BsrCode} not found");
                    }
                }

                // If no request or request not found, pick random song
                if (nextLevel == null)
                {
                    nextLevel = GetRandomLevel();
                }

                if (nextLevel == null)
                {
                    Plugin.Log.Error("GameplayManager: No songs available to play!");
                    yield break;
                }

                // Play the song
                yield return StartCoroutine(PlayLevel(nextLevel, requestInfo));

                // Wait a bit before next song
                yield return new WaitForSeconds(2f);
            }

            // Time's up!
            Plugin.Log.Info("GameplayManager: Time's up! Ending session");
            StopEndlessMode();
        }

        /// <summary>
        /// Timer countdown coroutine
        /// </summary>
        private IEnumerator TimerCountdown()
        {
            while (_isPlaying && _remainingTime > 0)
            {
                _remainingTime -= Time.deltaTime;

                // Log every minute
                if (Mathf.FloorToInt(_remainingTime) % 60 == 0)
                {
                    int minutesLeft = Mathf.FloorToInt(_remainingTime / 60f);
                    Plugin.Log.Info($"GameplayManager: {minutesLeft} minutes remaining");
                }

                yield return null;
            }
        }

        /// <summary>
        /// Play a specific level
        /// </summary>
        private IEnumerator PlayLevel(BeatmapLevel level, string announceMessage = null)
        {
            _isLoadingSong = true;
            Plugin.Log.Info($"GameplayManager: Playing level: {level.songName}");
            if (!string.IsNullOrEmpty(announceMessage))
                Chat.ChatManager.GetInstance().SendChatMessage(announceMessage);

            _playedLevelIds.Add(level.levelID);

            // Find the Standard characteristic
            BeatmapCharacteristicSO standardCharacteristic =
                Resources.FindObjectsOfTypeAll<BeatmapCharacteristicSO>()
                    .FirstOrDefault(x => x.serializedName == "Standard");

            if (standardCharacteristic == null)
            {
                Plugin.Log.Error("GameplayManager: Could not find Standard characteristic");
                _isLoadingSong = false;
                yield break;
            }

            // Get available difficulties for Standard
            var difficulties = level.GetDifficulties(standardCharacteristic)?.ToArray();
            if (difficulties == null || difficulties.Length == 0)
            {
                Plugin.Log.Error($"GameplayManager: No difficulties found for {level.songName}");
                _isLoadingSong = false;
                yield break;
            }

            var randomDiff = difficulties[UnityEngine.Random.Range(0, difficulties.Length)];

            try
            {
                // *** CHANGED: Use captured settings instead of empty ones ***
                var gameplayModifiers = _capturedModifiers ?? CreateDefaultNoFailModifiers();
                var playerSettings = _capturedPlayerSettings ?? new PlayerSpecificSettings();

                Plugin.Log.Info($"GameplayManager: Starting level with captured modifiers (No Fail: {gameplayModifiers.noFailOn0Energy})");

                OverrideEnvironmentSettings overrideEnvironmentSettings = null;
                var beatmapKey = new BeatmapKey(
                    level.levelID,
                    standardCharacteristic,
                    randomDiff
                );

                _menuTransitionsHelper.StartStandardLevel(
                    gameMode: "Solo",
                    beatmapKey: beatmapKey,
                    beatmapLevel: level,
                    overrideEnvironmentSettings: overrideEnvironmentSettings,
                    playerOverrideColorScheme: _capturedColorScheme, // Use captured color scheme
                    playerOverrideLightshowColors: false,
                    beatmapOverrideColorScheme: null,
                    gameplayModifiers: gameplayModifiers, // *** Use captured modifiers ***
                    playerSpecificSettings: playerSettings, // *** Use captured player settings ***
                    practiceSettings: null,
                    environmentsListModel: _environmentsListModel,
                    backButtonText: "Menu",
                    useTestNoteCutSoundEffects: false,
                    startPaused: false,
                    beforeSceneSwitchToGameplayCallback: null,
                    afterSceneSwitchToGameplayCallback: null,
                    levelFinishedCallback: (data, results) =>
                    {
                        Plugin.Log.Info("GameplayManager: Level completed/failed");
                        _isLoadingSong = false;
                    },
                    levelRestartedCallback: null,
                    recordingToolData: null
                );
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"GameplayManager: Error starting level:\n{ex}");
                _isLoadingSong = false;
            }

            // Approximate wait: use song duration
            float songDuration = level.songDuration;
            yield return new WaitForSeconds(songDuration);
        }



        /// <summary>
        /// Get a random level that hasn't been played recently
        /// </summary>
        private BeatmapLevel GetRandomLevel()
        {
            // Filter out recently played songs
            var availableToPlay = _availableLevels
                .Where(l => !_playedLevelIds.Contains(l.levelID))
                .ToList();

            // If all songs played, reset the history
            if (availableToPlay.Count == 0)
            {
                Plugin.Log.Info("GameplayManager: All songs played, resetting history");
                _playedLevelIds.Clear();
                availableToPlay = _availableLevels;
            }

            if (availableToPlay.Count == 0)
                return null;

            int randomIndex = UnityEngine.Random.Range(0, availableToPlay.Count);
            return availableToPlay[randomIndex];
        }

        /// <summary>
        /// Find a level by BSR code
        /// </summary>
        private BeatmapLevel FindLevelByBsr(string bsrCode)
        {
            // BSR codes are typically the last part of BeatSaver IDs
            // Level IDs in Beat Saber are formatted as: custom_level_<hash>
            // We need to search for levels that match the BSR code

            bsrCode = bsrCode.ToLower().Replace("!", "").Trim();

            foreach (var level in _availableLevels)
            {
                // Check if level ID contains the BSR code
                if (level.levelID.ToLower().Contains(bsrCode))
                {
                    return level;
                }

                // Also check custom data if available
                // This is where BeatSaver metadata is stored
            }

            Plugin.Log.Warn($"GameplayManager: Could not find level with BSR code: {bsrCode}");
            return null;
        }

        /// <summary>
        /// Capture current player settings and modifiers from the menu
        /// </summary>
        private void CapturePlayerSettings()
        {
            try
            {
                // Find the gameplay setup view controller to get current settings
                var gameplaySetup = Resources.FindObjectsOfTypeAll<GameplaySetupViewController>()
                    .FirstOrDefault();

                if (gameplaySetup != null)
                {
                    // Capture gameplay modifiers (left menu settings)
                    _capturedModifiers = gameplaySetup.gameplayModifiers;

                    // Force No Fail to be enabled
                    _capturedModifiers = _capturedModifiers.CopyWith(noFailOn0Energy: true);

                    Plugin.Log.Info("GameplayManager: Captured gameplay modifiers from menu");
                    Plugin.Log.Info($"  - No Fail: {_capturedModifiers.noFailOn0Energy}");
                    Plugin.Log.Info($"  - Faster Song: {_capturedModifiers.songSpeed}");
                    Plugin.Log.Info($"  - Disappearing Arrows: {_capturedModifiers.disappearingArrows}");
                    Plugin.Log.Info($"  - Ghost Notes: {_capturedModifiers.ghostNotes}");
                }
                else
                {
                    Plugin.Log.Warn("GameplayManager: Could not find GameplaySetupViewController, using defaults with No Fail");
                    _capturedModifiers = CreateDefaultNoFailModifiers();
                }

                // Capture player specific settings
                var playerDataModel = Resources.FindObjectsOfTypeAll<PlayerDataModel>()
                    .FirstOrDefault();

                if (playerDataModel != null)
                {
                    var playerData = playerDataModel.playerData;
                    _capturedPlayerSettings = playerData.playerSpecificSettings;
                    _capturedColorScheme = playerData.colorSchemesSettings.GetSelectedColorScheme();

                    Plugin.Log.Info("GameplayManager: Captured player-specific settings");
                    Plugin.Log.Info($"  - Left Handed: {_capturedPlayerSettings.leftHanded}");
                    Plugin.Log.Info($"  - Player Height: {_capturedPlayerSettings.playerHeight}");
                    Plugin.Log.Info($"  - Auto Restart: {_capturedPlayerSettings.autoRestart}");
                }
                else
                {
                    Plugin.Log.Warn("GameplayManager: Could not find PlayerDataModel, using default player settings");
                    _capturedPlayerSettings = new PlayerSpecificSettings();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"GameplayManager: Error capturing player settings: {ex.Message}");
                // Fallback to defaults with No Fail enabled
                _capturedModifiers = CreateDefaultNoFailModifiers();
                _capturedPlayerSettings = new PlayerSpecificSettings();
            }
        }


        public void Shutdown()
        {
            Plugin.Log.Info("GameplayManager: Shutting down...");
            StopEndlessMode();
        }

        private bool EnsureDependencies()
        {
            if (_menuTransitionsHelper == null)
            {
                _menuTransitionsHelper = Resources.FindObjectsOfTypeAll<MenuTransitionsHelper>()
                                                  .FirstOrDefault();
                if (_menuTransitionsHelper == null)
                {
                    Plugin.Log.Error("GameplayManager: Could not find MenuTransitionsHelper in scene");
                    return false;
                }
                Plugin.Log.Info("GameplayManager: Resolved MenuTransitionsHelper");
            }

            if (_environmentsListModel == null)
            {
               
                if (_environmentsListModel == null)
                {
                    Plugin.Log.Error("GameplayManager: Could not find EnvironmentsListModel in scene");
                    return false;
                }

                Plugin.Log.Info("GameplayManager: Resolved EnvironmentsListModel");
            }

            return true;
        }

    }

    /// <summary>
    /// Represents a song request from chat
    /// </summary>
    public class SongRequest
    {
        public string BsrCode { get; set; }
        public string RequesterName { get; set; }
        public DateTime RequestTime { get; set; }
    }
}