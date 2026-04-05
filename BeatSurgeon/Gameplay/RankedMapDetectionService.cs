using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Utils;
using SongCore;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Queries BeatLeader and ScoreSaber APIs to detect whether the currently-selected
    /// beatmap difficulty is ranked, and exposes a gate flag used by the command and
    /// channel-point subsystems to block all effects on ranked play.
    /// </summary>
    internal sealed class RankedMapDetectionService : IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("RankedMapDetection");

        // Shared HttpClient — single instance is the .NET recommended pattern.
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // Lightweight JSON presence checks (no JSON library dependency).
        // BeatLeader ranked status == 3
        private static readonly Regex _blRankedRegex =
            new Regex("\"status\"\\s*:\\s*3", RegexOptions.Compiled);
        // ScoreSaber "ranked":true
        private static readonly Regex _ssRankedRegex =
            new Regex("\"ranked\"\\s*:\\s*true", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Persistent cross-session cache: lowercase hash → is ranked
        private readonly ConcurrentDictionary<string, bool> _cache =
            new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        private volatile bool _isChecking;
        private volatile bool _isRanked;
        private bool _isInGameplay;
        private string _lastCheckedHash;
        private CancellationTokenSource _checkCts;

        private static RankedMapDetectionService _instance;

        internal static RankedMapDetectionService Instance =>
            _instance ?? (_instance = new RankedMapDetectionService());

        public RankedMapDetectionService()
        {
            _instance = this;
        }

        /// <summary>
        /// True when all commands and channel-point redemptions should be blocked:
        /// either a ranked result has been confirmed, or the API check is still in-flight.
        /// Always returns false when DisableOnRanked is off in config.
        /// </summary>
        internal bool IsCurrentMapRankedOrChecking =>
            PluginConfig.Instance?.DisableOnRanked == true && (_isRanked || _isChecking);

        /// <summary>Called from the menu Harmony patch (HandleDidChangeDifficultyBeatmap) for early pre-check.</summary>
        internal void StartPreCheck(BeatmapKey beatmapKey)
        {
            if (PluginConfig.Instance?.DisableOnRanked != true) return;
            BeginCheck(beatmapKey);
        }

        /// <summary>Called from the GameplayCoreInstaller patch once the GameCore scene is set up.</summary>
        internal void OnGameCoreLoaded(BeatmapKey beatmapKey)
        {
            // Always call regardless of config so state is correct if the toggle is flipped mid-session.
            _isInGameplay = true;
            BeginCheck(beatmapKey);
        }

        /// <summary>Call when leaving a GameCore scene to reset per-song state.</summary>
        internal void Reset()
        {
            _log.Info("RankedMapDetectionService: Reset (scene exit)");
            _checkCts?.Cancel();
            _isChecking = false;
            _isRanked = false;
            _isInGameplay = false;
            _lastCheckedHash = null;
        }

        public void Dispose()
        {
            Reset();
            _instance = null;
        }

        // ─────────────────────────── Internal logic ───────────────────────────

        private void BeginCheck(BeatmapKey beatmapKey)
        {
            string levelId = beatmapKey.levelId ?? string.Empty;

            // OST / DLC levels never appear on ranked leaderboards — skip the API call.
            if (!levelId.StartsWith("custom_level_", StringComparison.OrdinalIgnoreCase))
            {
                _log.Debug("Not a custom level – skipping ranked check: " + levelId);
                _isRanked = false;
                _isChecking = false;
                _lastCheckedHash = null;
                return;
            }

            string hash = Collections.GetCustomLevelHash(levelId);
            if (string.IsNullOrEmpty(hash))
            {
                _log.Warn("Could not resolve hash for levelId=" + levelId + " – treating as unranked");
                _isRanked = false;
                _isChecking = false;
                return;
            }

            hash = hash.ToLowerInvariant();

            // Compute difficulty/mode first so the cache key is diff-aware.
            int diffNum = DifficultyToApiNumber(beatmapKey.difficulty);
            string modeStr = beatmapKey.beatmapCharacteristic?.serializedName ?? "Standard";
            string cacheKey = hash + "|" + diffNum + "|" + modeStr;

            // Cache hit → use immediately without another network round-trip.
            if (_cache.TryGetValue(cacheKey, out bool cached))
            {
                _log.Debug("Cache hit: hash=" + hash + " diff=" + diffNum + " mode=" + modeStr + " ranked=" + cached);
                _isRanked = cached;
                _isChecking = false;
                _lastCheckedHash = cacheKey;
                if (cached) NotifyIfRanked();
                return;
            }

            // Same hash+diff+mode already in-flight → don't duplicate the work.
            if (_lastCheckedHash == cacheKey && _isChecking)
            {
                _log.Debug("Already checking hash=" + hash + " diff=" + diffNum + " mode=" + modeStr);
                return;
            }

            // Different map/difficulty → cancel the old check and start fresh.
            _checkCts?.Cancel();
            _checkCts = new CancellationTokenSource();
            _lastCheckedHash = cacheKey;
            _isChecking = true;
            _isRanked = false;

            _log.Info("Starting ranked check: hash=" + hash + " diff=" + diffNum + " mode=" + modeStr);

            // Fire-and-forget; CheckRankedAsync updates state when done.
            _ = CheckRankedAsync(hash, diffNum, modeStr, cacheKey, _checkCts.Token);
        }

        private async Task CheckRankedAsync(string hash, int diffNum, string modeStr, string cacheKey, CancellationToken ct)
        {
            bool blRanked = false;
            bool ssRanked = false;

            try
            {
                // Run both checks in parallel to minimise latency.
                Task<bool> blTask = (PluginConfig.Instance?.DisableOnRankedBL == true)
                    ? CheckBeatLeaderAsync(hash, diffNum, modeStr, ct)
                    : Task.FromResult(false);
                Task<bool> ssTask = (PluginConfig.Instance?.DisableOnRankedSS == true)
                    ? CheckScoreSaberAsync(hash, diffNum, modeStr, ct)
                    : Task.FromResult(false);

                await Task.WhenAll(blTask, ssTask).ConfigureAwait(false);

                blRanked = blTask.Status == TaskStatus.RanToCompletion && blTask.Result;
                ssRanked = ssTask.Status == TaskStatus.RanToCompletion && ssTask.Result;
            }
            catch (Exception ex)
            {
                _log.Warn("CheckRankedAsync unexpected error for hash=" + hash + ": " + ex.Message);
            }

            // Discard the result if this check was superseded by a newer one.
            if (ct.IsCancellationRequested) return;

            bool ranked = blRanked || ssRanked;
            _log.Info("Ranked check complete: hash=" + hash + " BL=" + blRanked + " SS=" + ssRanked + " → ranked=" + ranked);

            _cache[cacheKey] = ranked;
            _isRanked = ranked;
            _isChecking = false;

            if (ranked) NotifyIfRanked();
        }

        private static async Task<bool> CheckBeatLeaderAsync(string hash, int diffNum, string modeStr, CancellationToken ct)
        {
            try
            {
                // BeatLeader API requires uppercase hash and path-segment parameters (no /v1/ prefix).
                string url = "https://api.beatleader.xyz/leaderboard/hash/" + hash.ToUpperInvariant()
                             + "/" + diffNum + "/" + modeStr;

                HttpResponseMessage response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                bool ranked = _blRankedRegex.IsMatch(json);
                _log.Debug("BeatLeader: hash=" + hash + " ranked=" + ranked);
                return ranked;
            }
            catch (Exception ex)
            {
                // Fail-open: network error / 404 / timeout / cancellation → treat as unranked.
                _log.Warn("BeatLeader check failed for hash=" + hash + ": " + ex.Message);
                return false;
            }
        }

        private static async Task<bool> CheckScoreSaberAsync(string hash, int diffNum, string modeStr, CancellationToken ct)
        {
            try
            {
                string url = "https://scoresaber.com/api/leaderboard/by-hash/" + hash
                             + "/info?difficulty=" + diffNum + "&gameMode=Solo" + modeStr;

                HttpResponseMessage response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                bool ranked = _ssRankedRegex.IsMatch(json);
                _log.Debug("ScoreSaber: hash=" + hash + " ranked=" + ranked);
                return ranked;
            }
            catch (Exception ex)
            {
                // Fail-open.
                _log.Warn("ScoreSaber check failed for hash=" + hash + ": " + ex.Message);
                return false;
            }
        }

        private static void NotifyIfRanked()
        {
            try
            {
                if (!_instance?._isInGameplay == true) return;
                if (PluginConfig.Instance?.NotifyOnRankedDisable == true)
                {
                    ChatManager.GetInstance()?.SendMutedChatMessage(
                        "BeatSurgeon detected a ranked map. All sabotage disabled.");
                }
            }
            catch { }
        }

        private static int DifficultyToApiNumber(BeatmapDifficulty d)
        {
            switch (d)
            {
                case BeatmapDifficulty.Easy:       return 1;
                case BeatmapDifficulty.Normal:     return 3;
                case BeatmapDifficulty.Hard:       return 5;
                case BeatmapDifficulty.Expert:     return 7;
                case BeatmapDifficulty.ExpertPlus: return 9;
                default:                           return 1;
            }
        }
    }
}
