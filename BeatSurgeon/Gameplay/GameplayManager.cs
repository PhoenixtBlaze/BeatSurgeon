using IPA.Utilities;
using BeatSurgeon.Chat;
using BeatSurgeon.HarmonyPatches;
using BeatSurgeon.Integrations;
using SongCore;
using SongCore.Utilities;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Zenject;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;


namespace BeatSurgeon.Gameplay
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
        [Inject] private EnvironmentsListModel _environmentsListModel;

        private static GameplayManager _instance;
        private static GameObject _persistentGO;

        // Dependencies
        private MenuTransitionsHelper _menuTransitionsHelper;
        [Inject] private BeatmapLevelsModel _beatmapLevelsModel;
        private IBeatmapLevelData _preloadedLevelData;

        private GameplayModifiers _capturedModifiers;
        private PlayerSpecificSettings _capturedPlayerSettings;
        private ColorScheme _capturedColorScheme;

        // State
        private bool _isPlaying = false;
        private float _remainingTime = 0f;
        private float _totalTime = 0f;
        private int _lastLoggedWholeMinutes = -1;
        private readonly List<BeatmapLevel> _availableLevels = new List<BeatmapLevel>();
        private readonly List<string> _playedLevelIds = new List<string>();

        private bool _isMultiplayerScene = false;


        // “recently requested/played” window for requeue blocking
        private readonly Queue<string> _recentRequestOrPlayHistory = new Queue<string>();

        private bool _isLoadingSong = false;

        // Track why the last level ended
        private LevelCompletionResults.LevelEndAction _lastLevelEndAction = LevelCompletionResults.LevelEndAction.None;

        private InLevelQueueProcessor _inLevelQueueProcessor;

        // Track the currently-started request (if the current song came from queue)
        private SongRequest _currentSongRequest;

        public float? SwitchAfterSeconds { get; set; }  // e.g., 60 means "switch in 60s of current song"




        // ---- Download/pending-request state ----
        private sealed class PendingDownload
        {
            public SongRequest Request;
            public bool DownloadStarted;
            public bool HashFetchStarted;
            public string Hash;                 // 40-hex BeatSaver hash (lowercase)
            public string ResolvedLevelId;      // once installed + loaded
            public DateTime FirstSeenUtc;
            public DateTime LastDownloadAttemptUtc;
            public string LastError;
        }

        private readonly ConcurrentQueue<SongRequest> _requestQueue = new ConcurrentQueue<SongRequest>();
        private readonly ConcurrentDictionary<string, PendingDownload> _pendingDownloads = new ConcurrentDictionary<string, PendingDownload>(StringComparer.OrdinalIgnoreCase);

        private Coroutine _downloadPollerCoroutine;
        private float _nextSongCoreRefreshTime;

        // Pull hash from BeatSaver JSON without taking a JSON dependency.
        private static readonly System.Text.RegularExpressions.Regex HashRegex =
            new System.Text.RegularExpressions.Regex("\"hash\"\\s*:\\s*\"([0-9a-fA-F]{40})\"",
                System.Text.RegularExpressions.RegexOptions.Compiled);



        // A “default with No Fail on” set of modifiers
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
                false                                      // zenMode (or last bool)
            );
        }

        // Coroutines
        private Coroutine _gameplayCoroutine;
        private Coroutine _timerCoroutine;

        public static GameplayManager GetInstance()
        {
            if (_instance == null)
            {
                _persistentGO = new GameObject("BeatSurgeon_GameplayManager_GO");
                DontDestroyOnLoad(_persistentGO);
                _instance = _persistentGO.AddComponent<GameplayManager>();
                // Create the in-level tick processor once and keep it alive across scenes
                var qp = _persistentGO.GetComponent<InLevelQueueProcessor>();
                if (qp == null)
                    qp = _persistentGO.AddComponent<InLevelQueueProcessor>();

                _instance._inLevelQueueProcessor = qp;
                _instance._inLevelQueueProcessor.Initialize(_instance);
                Plugin.Log.Info("GameplayManager: Created new instance");
            }
            return _instance;
        }


        public bool PeekNextRequest(out SongRequest req)
        {
            return _requestQueue.TryPeek(out req);
        }

        private void UpdateSceneState()
        {
            _isMultiplayerScene = SceneManager.GetActiveScene().name.Contains("Multiplayer");
            // Or check: Resources.FindObjectsOfTypeAll<MultiplayerLocalActivePlayerFacade>().Length > 0;
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


            LogUtils.Debug(() => $"GameplayManager: Starting Endless Mode for {durationMinutes} minutes");

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

            _inLevelQueueProcessor?.StartProcessing();
            // *** NEW: Subscribe to mid-song switch events ***
            _inLevelQueueProcessor.SwitchRequested += OnSwitchRequestedDuringPlay;
            _inLevelQueueProcessor.PreloadRequested += OnPreloadRequested;
            _downloadPollerCoroutine = StartCoroutine(DownloadPoller());


            LogUtils.Debug(() => $"GameplayManager: Found {_availableLevels.Count} custom songs");

            // Start gameplay loop
            _gameplayCoroutine = StartCoroutine(GameplayLoop());
            _timerCoroutine = StartCoroutine(TimerCountdown());
        }


        private bool IsMultiplayerSession()
        {
            // Safe reflection-based detection - no DLL references needed
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Any(obj =>
                    obj != null &&
                    (obj.name.Contains("Multiplayer") ||
                     obj.GetType().Name.Contains("Multiplayer") ||
                     obj.GetType().FullName?.Contains("Multiplayer") == true)
                );
        }



        /// <summary>
        /// Stop the endless mode
        /// </summary>
        public void StopEndlessMode()
        {
            LogUtils.Debug(() => "GameplayManager: Stopping Endless Mode");
            _isPlaying = false;
            _remainingTime = 0f;
            FasterSongPatch.ClearCache();

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
            if (_downloadPollerCoroutine != null)
            {
                StopCoroutine(_downloadPollerCoroutine);
                _downloadPollerCoroutine = null;
            }

            _pendingDownloads.Clear();
            _playedLevelIds.Clear();
            _requestQueue.Clear();
            _inLevelQueueProcessor?.StopProcessing();
            _inLevelQueueProcessor.SwitchRequested -= OnSwitchRequestedDuringPlay; // cleanup
            _inLevelQueueProcessor.PreloadRequested -= OnPreloadRequested;
            _preloadedLevelData = null;
            _currentSongRequest = null;


            LogUtils.Debug(() => "GameplayManager: Endless Mode stopped");
        }


        private async void OnPreloadRequested(SongRequest nextRequest)
        {
            if (nextRequest == null) return;

            LogUtils.Debug(() => $"GameplayManager: Preloading assets for {nextRequest.BsrCode}...");

            if (TryResolveRequestToLevel(nextRequest, out var level))
            {
                try
                {
                    // This forces the game to load the audio and map data into memory/cache.
                    // When ReplaceScenes runs 5 seconds later, it will find this data instantly.
                    var result = await _beatmapLevelsModel.LoadBeatmapLevelDataAsync(level.levelID,BeatmapLevelDataVersion.Original,CancellationToken.None);
                    if (result.beatmapLevelData != null)
                    {
                        _preloadedLevelData = result.beatmapLevelData;
                        LogUtils.Debug(() => $"GameplayManager: Preload success for {level.songName}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warn($"GameplayManager: Preload failed: {ex.Message}");
                }
            }
        }


        public bool TryPrepareNextChain(
    out BeatmapLevel nextLevel,
    out BeatmapKey nextKey,
    out GameplayModifiers modifiers,
    out PlayerSpecificSettings playerSettings,
    out ColorScheme color,
    out EnvironmentsListModel envs)
        {
            nextLevel = null;
            nextKey = default;
            modifiers = _capturedModifiers ?? CreateDefaultNoFailModifiers();
            playerSettings = _capturedPlayerSettings ?? new PlayerSpecificSettings();
            color = _capturedColorScheme;
            envs = _environmentsListModel;

            if (!_isPlaying)
                return false;

            // FIX: Use TryDequeue instead of Dequeue
            int safety = _requestQueue.Count;
            while (safety-- > 0 && !_requestQueue.IsEmpty)
            {
                if (_requestQueue.TryDequeue(out var req))
                {
                    if (req == null) continue;

                    if (TryResolveRequestToLevel(req, out var resolvedLevel))
                    {
                        _currentSongRequest = req;
                        return BuildKeyForLevel(req, resolvedLevel, out nextLevel, out nextKey);
                    }

                    // Not installed yet -> ensure download started and re-enqueue
                    EnsureDownloadStarted(req);
                    _requestQueue.Enqueue(req);
                }
            }

            // Random fallback
            _currentSongRequest = null;
            var random = GetRandomLevel();
            if (random == null)
                return false;

            return BuildKeyForLevel(null, random, out nextLevel, out nextKey);
        }


        private bool BuildKeyForLevel(SongRequest req, BeatmapLevel level, out BeatmapLevel nextLevel, out BeatmapKey nextKey)
        {
            nextLevel = null;
            nextKey = default;

            var standardCharacteristic =
                Resources.FindObjectsOfTypeAll<BeatmapCharacteristicSO>()
                    .FirstOrDefault(x => x.serializedName == "Standard");

            if (standardCharacteristic == null)
                return false;

            var diffs = level.GetDifficulties(standardCharacteristic)?.ToArray();
            if (diffs == null || diffs.Length == 0)
                return false;

            var selectedDiff = diffs[UnityEngine.Random.Range(0, diffs.Length)];
            if (req?.RequestedDifficulty.HasValue == true && diffs.Contains(req.RequestedDifficulty.Value))
                selectedDiff = req.RequestedDifficulty.Value;

            _playedLevelIds.Add(level.levelID);

            nextLevel = level;
            nextKey = new BeatmapKey(level.levelID, standardCharacteristic, selectedDiff);
            return true;
        }

        private bool TryResolveRequestToLevel(SongRequest req, out BeatmapLevel level)
        {
            level = null;
            if (req == null) return false;

            string key = NormalizeKey(req.BsrCode);

            // If we already resolved it via hash->levelID, grab it directly.
            if (_pendingDownloads.TryGetValue(key, out var pd) && !string.IsNullOrWhiteSpace(pd.ResolvedLevelId))
            {
                var found = SongCore.Loader.GetLevelById(pd.ResolvedLevelId);
                if (found != null)
                {
                    level = found;
                    return true;
                }
            }


            // If the user pasted a 40-hex hash instead of a BSR key, support it:
            if (key.Length == 40 && key.All(Uri.IsHexDigit))
            {
                return TryFindLevelByHash(key, out level);
            }

            // No reliable way to match BeatSaver key to levelID without fetching its hash,
            // so at this point we return false and rely on download+poll to resolve later.
            return false;
        }

        private void EnsureDownloadStarted(SongRequest req)
        {
            string key = NormalizeKey(req.BsrCode);
            if (string.IsNullOrWhiteSpace(key)) return;

            if (!_pendingDownloads.TryGetValue(key, out var pd))
            {
                pd = new PendingDownload
                {
                    Request = req,
                    FirstSeenUtc = DateTime.UtcNow
                };
                _pendingDownloads[key] = pd;
            }

            if (pd.DownloadStarted)
                return;

            // Start download (once).
            pd.LastDownloadAttemptUtc = DateTime.UtcNow;

            pd.DownloadStarted = true;
            pd.LastDownloadAttemptUtc = DateTime.UtcNow;
            pd.LastError = null;

            StartCoroutine(DownloadInstallAndFinalizeCoroutine(key, pd));
        }

        private IEnumerator DownloadInstallAndFinalizeCoroutine(string key, PendingDownload pd)
        {
            // 1) Fetch metadata
            using (var metaReq = BeatSaverClient.GetMapMetadata(key))
            {
                metaReq.timeout = 15;
                yield return metaReq.SendWebRequest();

                if (metaReq.result != UnityWebRequest.Result.Success)
                {
                    pd.LastError = $"BeatSaver metadata failed: {metaReq.error}";
                    Plugin.Log.Warn($"GameplayManager: {pd.LastError}");
                    yield break;
                }

                var json = metaReq.downloadHandler.text;
                if (!BeatSaverClient.TryParse(json, out var hashLower, out var downloadUrl, out var songName, out var levelAuthor))
                {
                    pd.LastError = "BeatSaver metadata parse failed (hash/downloadURL missing).";
                    Plugin.Log.Warn($"GameplayManager: {pd.LastError}");
                    yield break;
                }

                pd.Hash = hashLower;

                // If already installed, skip download
                if (TryFindLevelByHash(pd.Hash, out var already))
                {
                    pd.ResolvedLevelId = already.levelID;
                    FinalizeDownloadedSong(pd, already);
                    yield break;
                }

                // 2) Download zip
                using (var zipReq = UnityWebRequest.Get(downloadUrl))
                {
                    LogUtils.Debug(() => $"GameplayManager: Starting download/install for {key}");
                    zipReq.timeout = 60;
                    zipReq.downloadHandler = new DownloadHandlerBuffer();
                    yield return zipReq.SendWebRequest();

                    if (zipReq.result != UnityWebRequest.Result.Success)
                    {
                        pd.LastError = $"Zip download failed: {zipReq.error}";
                        Plugin.Log.Warn($"GameplayManager: {pd.LastError}");
                        yield break;
                    }

                    var zipBytes = zipReq.downloadHandler.data;
                    if (zipBytes == null || zipBytes.Length == 0)
                    {
                        pd.LastError = "Zip download returned empty data.";
                        yield break;
                    }

                    // 3) Extract to CustomLevels
                    string customSongsPath = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "CustomLevels"));
                    string folderName = $"{key} - {SongInstaller.Sanitize(songName)} - {SongInstaller.Sanitize(levelAuthor)}";
                    string destDir = Path.Combine(customSongsPath, folderName);

                    SongInstaller.SafeExtractZip(zipBytes, destDir);
                    LogUtils.Debug(() => $"GameplayManager: Extracted to {destDir}, calling SongCore.Loader.LoadCustomLevel...");

                    // Immediately load only this folder (no SongCore RefreshSongs needed).
                    var loaded = SongCore.Loader.LoadCustomLevel(destDir);
                    var loadResult = SongCore.Loader.LoadCustomLevel(destDir);
                    LogUtils.Debug(() => $"GameplayManager: LoadCustomLevel result HasValue={loadResult.HasValue}");
                    if (loaded.HasValue)
                    {
                        var (hash, beatmapLevel) = loaded.Value;
                        pd.Hash = hash?.ToLowerInvariant();
                        pd.ResolvedLevelId = beatmapLevel.levelID;
                        pd.LastError = null;

                        FinalizeDownloadedSong(pd, beatmapLevel);
                        yield break;
                    }

                    // If it failed, keep your old fallback path (optional).
                    pd.LastError = "Installed, but SongCore could not LoadCustomLevel from the extracted folder.";
                    Plugin.Log.Warn($"GameplayManager: {pd.LastError}");

                }
            }

            // 4) Refresh SongCore only AFTER install (no polling refresh loop)
            
            
        }

        private void FinalizeDownloadedSong(PendingDownload pd, BeatmapLevel level)
        {
            try
            {
                // Add to playlist
                var playlist = EndlessPlaylistService.GetOrCreate();
                EndlessPlaylistService.AddLevel(playlist, level);

                // FIX: Use TryRemove for ConcurrentDictionary
                string key = NormalizeKey(pd.Request?.BsrCode);
                _pendingDownloads.TryRemove(key, out _);

                LogUtils.Debug(() => $"GameplayManager: Finalized {key} -> {level.songName}");

                // Announce
                Chat.ChatManager.GetInstance().SendChatMessage($"Downloaded & Ready: {pd.Request.BsrCode} ({level.songName})");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"GameplayManager: Error finalizing song: {ex.Message}");
            }
        }



        private IEnumerator FetchBeatSaverHashCoroutine(string key, PendingDownload pd)
        {
            // If you later implement a built-in downloader, you can reuse this hash.
            string url = $"https://api.beatsaver.com/maps/id/{key}";

            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();

                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    pd.LastError = $"BeatSaver hash lookup failed: {req.error}";
                    yield break;
                }

                var text = req.downloadHandler.text ?? string.Empty;
                var m = HashRegex.Match(text);
                if (!m.Success)
                {
                    pd.LastError = "BeatSaver hash lookup failed: hash not found in response.";
                    yield break;
                }

                pd.Hash = m.Groups[1].Value.ToLowerInvariant();

                // If it already exists locally, resolve immediately.
                if (TryFindLevelByHash(pd.Hash, out var level))
                    pd.ResolvedLevelId = level.levelID;
            }
        }


        /// <summary>
        /// Called by InLevelQueueProcessor when it's time to switch mid-song to a new request.
        /// </summary>
        private void OnSwitchRequestedDuringPlay(SongRequest nextRequest)
        {
            if (nextRequest == null || !_isPlaying)
                return;

            LogUtils.Debug(() => $"GameplayManager: Switch triggered for {nextRequest.BsrCode} by {nextRequest.RequesterName}");

            // Resolve the request to an actual level
            if (!TryResolveRequestToLevel(nextRequest, out var resolvedLevel))
            {
                Plugin.Log.Warn($"GameplayManager: Cannot switch — {nextRequest.BsrCode} still not playable");
                ChatManager.GetInstance().SendChatMessage($"!!Switch failed — {nextRequest.BsrCode} not ready");
                return;
            }

            // Build the beatmap key
            if (!BuildKeyForLevel(nextRequest, resolvedLevel, out var nextLevel, out var nextKey))
            {
                Plugin.Log.Error($"GameplayManager: Failed to build key for {nextRequest.BsrCode}");
                return;
            }

            // *** Trigger the chain transition (same as your Harmony patch does at song end) ***
            var modifiers = _capturedModifiers ?? CreateDefaultNoFailModifiers();
            var playerSettings = _capturedPlayerSettings ?? new PlayerSpecificSettings();

            LogUtils.Debug(() => $"GameplayManager: Executing mid-song chain to {nextLevel.songName}");
            ChatManager.GetInstance().SendChatMessage($"!!Now playing: {nextLevel.songName} ({nextRequest.RequesterName})");

            // Track the new request as current
            _currentSongRequest = nextRequest;

            // Arm for next switch if this request also has SwitchAfterSeconds
            _inLevelQueueProcessor?.ArmForCurrentSegment(nextRequest.SwitchAfterSeconds);

            // Use your existing chain mechanism from EndlessHarmonyPatch
            EndlessHarmonyPatch.ReplaceScenes(
                _menuTransitionsHelper,
                nextLevel,
                nextKey,
                modifiers,
                playerSettings,
                _capturedColorScheme,
                _environmentsListModel
            );

        }



        /// <summary>
        /// Queue a song request from chat (BSR code)
        /// </summary>
        public bool TryQueueSongRequest(
        string bsrCode,
        string requesterName,
        BeatmapDifficulty? requestedDifficulty,
        float? startTimeSeconds,
        float? switchAfterSeconds,
        float? segmentLengthSeconds,
        out string rejectReason)
        {
            rejectReason = null;

            if (!_isPlaying)
            {
                rejectReason = "Endless mode is not running.";
                return false;
            }

            var cfg = Plugin.Settings;
            if (cfg != null && !cfg.SongRequestsEnabled)
            {
                rejectReason = "Song requests are disabled.";
                return false;
            }

            bsrCode = (bsrCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(bsrCode))
            {
                rejectReason = "Missing BSR code.";
                return false;
            }

            int sizeLimit = cfg?.QueueSizeLimit ?? 20;
            if (sizeLimit > 0 && _requestQueue.Count >= sizeLimit)
            {
                rejectReason = "Request queue is full.";
                return false;
            }

            int requeueLimit = cfg?.RequeueLimit ?? 10;
            if (requeueLimit > 0 &&
                _recentRequestOrPlayHistory.Contains(bsrCode, StringComparer.OrdinalIgnoreCase))
            {
                rejectReason = "That song was requested/played too recently.";
                return false;
            }

            var req = new SongRequest
            {
                BsrCode = bsrCode,
                RequesterName = string.IsNullOrWhiteSpace(requesterName) ? "Unknown" : requesterName,
                RequestTime = DateTime.UtcNow,

                RequestedDifficulty = requestedDifficulty,
                StartTimeSeconds = startTimeSeconds,
                SegmentLengthSeconds = segmentLengthSeconds,

                
                SwitchAfterSeconds = switchAfterSeconds
            };

            _requestQueue.Enqueue(req);
            EnsureDownloadStarted(req);

            if (requeueLimit > 0)
            {
                _recentRequestOrPlayHistory.Enqueue(bsrCode);
                while (_recentRequestOrPlayHistory.Count > requeueLimit)
                    _recentRequestOrPlayHistory.Dequeue();
            }

            LogUtils.Debug(() => $"GameplayManager: Queued request {bsrCode} by {req.RequesterName}");
            return true;
        }


        public void SetDependencies(MenuTransitionsHelper menuTransitionsHelper,
                            EnvironmentsListModel environmentsListModel)
        {
            _menuTransitionsHelper = menuTransitionsHelper;
            _environmentsListModel = environmentsListModel;

            LogUtils.Debug(() => "GameplayManager: Dependencies injected");
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
            LogUtils.Debug(() => "GameplayManager: Loading available songs from SongCore...");
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

                LogUtils.Debug(() => $"GameplayManager: Loaded {_availableLevels.Count} custom songs");
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
                    /*
                    var request = _requestQueue.Dequeue();
                    _currentSongRequest = request;
                    LogUtils.Debug(() => $"GameplayManager: Processing request {request.BsrCode}");

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
                    */
                }

                // If no request or request not found, pick random song
                if (nextLevel == null)
                {
                    _currentSongRequest = null;
                    nextLevel = GetRandomLevel();
                }

                if (nextLevel == null)
                {
                    Plugin.Log.Error("GameplayManager: No songs available to play!");
                    yield break;
                }

                // Play the song
                yield return StartCoroutine(PlayLevel(nextLevel, requestInfo));

                // Seamless chaining is handled by the Harmony patch; stop this loop after first song.
                yield break;
            }

            // Time's up!
            LogUtils.Debug(() => "GameplayManager: Time's up! Ending session");
            StopEndlessMode();
        }

        /// <summary>
        /// Timer countdown coroutine
        /// </summary>
        private IEnumerator TimerCountdown()
        {
            _lastLoggedWholeMinutes = -1;

            while (_isPlaying && _remainingTime > 0)
            {
                _remainingTime -= Time.deltaTime;

                int wholeMinutes = Mathf.Max(0, Mathf.FloorToInt(_remainingTime / 60f));
                if (wholeMinutes != _lastLoggedWholeMinutes)
                {
                    _lastLoggedWholeMinutes = wholeMinutes;
                    LogUtils.Debug(() => $"GameplayManager: {wholeMinutes} minutes remaining");
                }

                yield return null;
            }
        }

        private IEnumerator DownloadPoller()
        {
            _nextSongCoreRefreshTime = 0f;

            while (_isPlaying)
            {
                // Only refresh periodically; refresh is expensive.
                if (_pendingDownloads.Count > 0 && Time.unscaledTime >= _nextSongCoreRefreshTime)
                {
                    _nextSongCoreRefreshTime = Time.unscaledTime + 5f;

                    // Refresh SongCore’s loaded song list in-game.
                    SongCore.Loader.Instance.RefreshSongs(false); // important: makes new installs visible [file:152]
                    LoadAvailableSongs(); // rebuild your cached list from Loader.CustomLevels [file:147]
                }

                // Try to resolve any pending downloads into real BeatmapLevels.
                foreach (var kvp in _pendingDownloads.ToList())
                {
                    var pd = kvp.Value;
                    if (pd == null) continue;

                    if (!string.IsNullOrWhiteSpace(pd.ResolvedLevelId))
                        continue;

                    if (!string.IsNullOrWhiteSpace(pd.Hash) && TryFindLevelByHash(pd.Hash, out var level))
                    {
                        pd.ResolvedLevelId = level.levelID;
                        // Try to resolve any pending downloads into real BeatmapLevels.
                        // Try to resolve any pending downloads into real BeatmapLevels.
                        foreach (var entry in _pendingDownloads.ToList())
                        {
                            var key = entry.Key;
                            var pending = entry.Value;
                            if (pending == null)
                                continue;

                            // Already resolved → remove from dictionary so we stop refreshing for it.
                            if (!string.IsNullOrWhiteSpace(pending.ResolvedLevelId))
                            {
                                _pendingDownloads.TryRemove(key, out _);
                                continue;
                            }

                            // If we have a hash and SongCore knows about it, map to levelID.
                            if (!string.IsNullOrWhiteSpace(pending.Hash) &&
                                TryFindLevelByHash(pending.Hash, out var resolvedLevel))
                            {
                                pending.ResolvedLevelId = resolvedLevel.levelID;
                                pending.LastError = null;

                                LogUtils.Debug(() => $"GameplayManager: Pending download {pending.Request?.BsrCode} resolved to {pending.ResolvedLevelId}");

                                // Optional: auto-switch ASAP requests
                                bool shouldAutoSwitch = (pending.Request?.SwitchAfterSeconds ?? -1f) == 0f;
                                if (shouldAutoSwitch)
                                {
                                    LogUtils.Debug(() => $"GameplayManager: {pending.Request?.BsrCode} ready — arming immediate switch");
                                    _inLevelQueueProcessor?.ArmForCurrentSegment(0.1f);
                                }

                                // Now that it's resolved, we no longer need to track it here.
                                _requestQueue.TryDequeue(out _);
                            }
                        }

                        pd.LastError = null;

                        // Optional: chat announce once installed
                        Chat.ChatManager.GetInstance().SendChatMessage($"Downloaded & ready: {pd.Request.BsrCode}");
                    }
                }

                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private static string NormalizeKey(string key)
        {
            return (key ?? string.Empty).Trim().TrimStart('!').ToLowerInvariant();
        }

        private bool TryFindLevelByHash(string hashLower, out BeatmapLevel level)
        {
            level = null;
            if (string.IsNullOrWhiteSpace(hashLower)) return false;

            // SongCore handles hash->level internally (uses CustomLevelsById).
            var found = SongCore.Loader.GetLevelByHash(hashLower);
            if (found == null) return false;

            level = found;
            return true;
        }



        /// <summary>
        /// Play a specific level
        /// </summary>
        private IEnumerator PlayLevel(BeatmapLevel level, string announceMessage = null)
        {
            _isLoadingSong = true;
            _lastLevelEndAction = LevelCompletionResults.LevelEndAction.None;

            // Use SwitchAfterSeconds for mid-song switching (single time m:ss).
            // If you later implement segment insertion, you can arm with SegmentLengthSeconds too.
            _inLevelQueueProcessor?.ArmForCurrentSegment(_currentSongRequest?.SwitchAfterSeconds);

            LogUtils.Debug(() => $"GameplayManager: Playing level: {level.songName}");

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
                var gameplayModifiers = _capturedModifiers ?? CreateDefaultNoFailModifiers();
                var playerSettings = _capturedPlayerSettings ?? new PlayerSpecificSettings();

                LogUtils.Debug(() => 
                    $"GameplayManager: Starting level with captured modifiers (No Fail: {gameplayModifiers.noFailOn0Energy})");

                OverrideEnvironmentSettings overrideEnvironmentSettings = null;

                var beatmapKey = new BeatmapKey(
                    level.levelID,
                    standardCharacteristic,
                    randomDiff
                );

                _menuTransitionsHelper.StartStandardLevel(
                    "Solo",
                    in beatmapKey,
                    level,
                    overrideEnvironmentSettings,
                    _capturedColorScheme,
                    false,
                    null,
                    gameplayModifiers,
                    playerSettings,
                    null,
                    _environmentsListModel,
                    "Menu",
                    false,
                    false,
                    null,
                    null,
                    (data, results) =>
                    {
                        LogUtils.Debug(() => 
                            $"GameplayManager: Level finished. State={results.levelEndStateType}, Action={results.levelEndAction}");

                        _lastLevelEndAction = results.levelEndAction;
                        _isLoadingSong = false;
                    },
                    null,
                    null
                );
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"GameplayManager: Error starting level:\n{ex}");
                _isLoadingSong = false;
                yield break;
            }

            // Wait until the level actually finishes or endless mode stops
            while (_isLoadingSong && _isPlaying)
                yield return null;

            // If endless mode was stopped elsewhere (timer expired, manual stop), just exit
            if (!_isPlaying)
                yield break;

            // If player chose "Quit to Menu", stop endless mode instead of forcing another song
            if (_lastLevelEndAction == LevelCompletionResults.LevelEndAction.Quit)
            {
                LogUtils.Debug(() => "GameplayManager: Player quit to menu, stopping endless mode");
                StopEndlessMode();
                yield break;
            }

            // Otherwise (Cleared/Failed/Restart handled internally), just return to let GameplayLoop move on
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
                LogUtils.Debug(() => "GameplayManager: All songs played, resetting history");
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

                    LogUtils.Debug(() => "GameplayManager: Captured gameplay modifiers from menu");
                    LogUtils.Debug(() => $"  - No Fail: {_capturedModifiers.noFailOn0Energy}");
                    LogUtils.Debug(() => $"  - Faster Song: {_capturedModifiers.songSpeed}");
                    LogUtils.Debug(() => $"  - Disappearing Arrows: {_capturedModifiers.disappearingArrows}");
                    LogUtils.Debug(() => $"  - Ghost Notes: {_capturedModifiers.ghostNotes}");
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

                    LogUtils.Debug(() => "GameplayManager: Captured player-specific settings");
                    LogUtils.Debug(() => $"  - Left Handed: {_capturedPlayerSettings.leftHanded}");
                    LogUtils.Debug(() => $"  - Player Height: {_capturedPlayerSettings.playerHeight}");
                    LogUtils.Debug(() => $"  - Auto Restart: {_capturedPlayerSettings.autoRestart}");
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
            LogUtils.Debug(() => "GameplayManager: Shutting down...");
            StopEndlessMode();
        }

        private bool EnsureDependencies()
        {
            if (_menuTransitionsHelper == null)
                _menuTransitionsHelper = Resources.FindObjectsOfTypeAll<MenuTransitionsHelper>().FirstOrDefault();

            if (_menuTransitionsHelper == null)
            {
                Plugin.Log.Error("GameplayManager: Could not find MenuTransitionsHelper in scene");
                return false;
            }

            // EnvironmentsListModel is NOT a UnityEngine.Object, so we must obtain it indirectly.
            if (_environmentsListModel == null)
            {
                // Try getting it from the ProjectContext (standard Zenject way)
                var container = ProjectContext.Instance.Container;
                if (container.HasBinding<EnvironmentsListModel>())
                {
                    _environmentsListModel = container.Resolve<EnvironmentsListModel>();
                }
                else
                {
                    // Fallback: Use Reflection to steal it from SimpleLevelStarter if global resolve fails
                    var sls = Resources.FindObjectsOfTypeAll<SimpleLevelStarter>().FirstOrDefault();
                    if (sls != null)
                    {
                        var field = typeof(SimpleLevelStarter).GetField("environmentsListModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        _environmentsListModel = (EnvironmentsListModel)field?.GetValue(sls);
                    }
                }
            }

            if (_environmentsListModel == null)
                Plugin.Log.Warn("GameplayManager: EnvironmentsListModel still null; mid-song switching/chaining may fail.");

            return true;
        }




        public bool TryDequeueQueuedRequest(out SongRequest request)
        {
            /*
            request = null;
            if (_requestQueue == null || _requestQueue.Count == 0) return false;

            int safety = _requestQueue.Count;
            while (safety-- > 0 && _requestQueue.Count > 0)
            {
                var candidate = _requestQueue.Dequeue();
                if (candidate == null) continue;

                if (TryResolveRequestToLevel(candidate, out _))
                {
                    request = candidate;
                    return true;
                }

                // Not playable yet → keep it in the queue.
                _requestQueue.Enqueue(candidate);
            }
            */
            request = null;
            return false;
        }


    }
}