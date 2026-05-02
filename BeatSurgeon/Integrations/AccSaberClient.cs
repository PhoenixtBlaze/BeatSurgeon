using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Integrations
{
    /// <summary>
    /// Fetches the AccSaber Reloaded bulk ranked-difficulty list once at startup and exposes
    /// an O(1) <see cref="IsRanked"/> lookup safe to call from any thread.
    ///
    /// Endpoint: GET https://api.accsaber.com/v1/maps/difficulties/all
    /// No authentication required. Returns a flat JSON array — no pagination.
    /// </summary>
    internal sealed class AccSaberClient : IInitializable, IDisposable
    {
        internal const int CacheMaxAgeMinutes = 60;

        private static readonly LogUtil _log = LogUtil.GetLogger("AccSaber");

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        // Matches "songHash":"ABCDEF...", capturing the hash value.
        private static readonly Regex _hashRegex =
            new Regex("\"songHash\"\\s*:\\s*\"([0-9a-fA-F]+)\"", RegexOptions.Compiled);

        // Matches "difficulty":"EXPERT_PLUS" (any of the five AccSaber difficulty strings).
        private static readonly Regex _diffRegex =
            new Regex("\"difficulty\"\\s*:\\s*\"(EASY|NORMAL|HARD|EXPERT|EXPERT_PLUS)\"", RegexOptions.Compiled);

        // Beat Saber difficulty string → AccSaber API enum string.
        private static readonly Dictionary<string, string> _difficultyMap =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Easy",       "EASY"        },
                { "Normal",     "NORMAL"      },
                { "Hard",       "HARD"        },
                { "Expert",     "EXPERT"      },
                { "ExpertPlus", "EXPERT_PLUS" }
            };

        // HashSet<"UPPERCASEHASH|DIFFICULTY_STRING"> for O(1) lookup.
        private volatile HashSet<string> _rankedSet = new HashSet<string>(StringComparer.Ordinal);
        private DateTime _lastFetchUtc = DateTime.MinValue;
        private int _fetchInFlight; // 0 = idle, 1 = fetching (interlocked flag)

        private static AccSaberClient _instance;

        internal static AccSaberClient Instance => _instance;

        public AccSaberClient()
        {
            _instance = this;
        }

        // ─────────────────────────── IInitializable ───────────────────────────

        public void Initialize()
        {
            _ = RefreshAsync();
        }

        public void Dispose()
        {
            _instance = null;
        }

        // ─────────────────────────── Public API ───────────────────────────────

        /// <summary>
        /// Returns true when the given map+difficulty is in the AccSaber ranked list.
        /// <paramref name="songHash"/> is normalized to uppercase before lookup.
        /// <paramref name="difficultyLabel"/> must use Beat Saber naming (e.g. "ExpertPlus").
        /// Safe to call from any thread.
        /// </summary>
        internal bool IsRanked(string songHash, string difficultyLabel)
        {
            if (string.IsNullOrEmpty(songHash) || string.IsNullOrEmpty(difficultyLabel))
                return false;

            if (!_difficultyMap.TryGetValue(difficultyLabel, out string accSaberDiff))
                return false;

            string key = songHash.ToUpperInvariant() + "|" + accSaberDiff;
            return _rankedSet.Contains(key);
        }

        /// <summary>
        /// Fire-and-forget refresh. Skips if a fetch is already in-flight or the cache is still
        /// fresh. Logs success/failure internally.
        /// </summary>
        internal async Task RefreshAsync()
        {
            // Skip if cache is fresh.
            if (_lastFetchUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _lastFetchUtc).TotalMinutes < CacheMaxAgeMinutes)
            {
                return;
            }

            // Only one fetch at a time.
            if (Interlocked.CompareExchange(ref _fetchInFlight, 1, 0) != 0)
                return;

            try
            {
                await FetchAndCacheAsync().ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _fetchInFlight, 0);
            }
        }

        // ─────────────────────────── Internal logic ───────────────────────────

        private async Task FetchAndCacheAsync()
        {
            const string url = "https://api.accsaber.com/v1/maps/difficulties/all";
            _log.Info("[BeatSurgeon][AccSaber] Fetching ranked list from " + url);

            string json;
            try
            {
                HttpResponseMessage response = await _http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _log.Warn("[BeatSurgeon][AccSaber] Failed to fetch ranked list: HTTP " + (int)response.StatusCode);
                    _rankedSet = new HashSet<string>(StringComparer.Ordinal);
                    return;
                }

                json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("[BeatSurgeon][AccSaber] Failed to fetch ranked list: " + ex.Message);
                _rankedSet = new HashSet<string>(StringComparer.Ordinal);
                return;
            }

            var newSet = ParseRankedSet(json);
            _rankedSet = newSet;
            _lastFetchUtc = DateTime.UtcNow;
            _log.Info("[BeatSurgeon][AccSaber] Ranked list loaded: " + newSet.Count + " entries.");
        }

        /// <summary>
        /// Parses the JSON array by walking entry-by-entry with regex to avoid a JSON dependency.
        /// Each entry is a small object block; we extract songHash+difficulty pairs and build the set.
        /// </summary>
        private static HashSet<string> ParseRankedSet(string json)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(json))
                return result;

            // The array is a sequence of objects. We find each opening brace, extract the region
            // up to the closing brace, then pull hash + difficulty from that region.
            int pos = 0;
            while (pos < json.Length)
            {
                int start = json.IndexOf('{', pos);
                if (start < 0) break;

                int end = json.IndexOf('}', start);
                if (end < 0) break;

                string entry = json.Substring(start, end - start + 1);

                Match hm = _hashRegex.Match(entry);
                Match dm = _diffRegex.Match(entry);

                if (hm.Success && dm.Success)
                {
                    string hashUpper = hm.Groups[1].Value.ToUpperInvariant();
                    string diff = dm.Groups[1].Value;
                    result.Add(hashUpper + "|" + diff);
                }

                pos = end + 1;
            }

            return result;
        }
    }
}
