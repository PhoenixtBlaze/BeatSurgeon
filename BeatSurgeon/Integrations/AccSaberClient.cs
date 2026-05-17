using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BeatSurgeon.Integrations
{
    /// <summary>
    /// Fetches the AccSaber Reloaded bulk ranked-difficulty list once at startup and exposes
    /// an O(1) <see cref="IsRanked"/> lookup safe to call from any thread.
    ///
    /// Endpoint: GET https://accsaberreloaded.com/v1/maps/difficulties/all
    /// No authentication required. Returns a flat JSON array — no pagination.
    /// </summary>
    internal sealed class AccSaberClient : IInitializable, IDisposable
    {
        internal const int CacheMaxAgeMinutes = 60;

        private static readonly string[] _bulkRankedListUrls =
        {
            "https://accsaberreloaded.com/v1/maps/difficulties/all",
            "https://accsaber.com/api/v1/maps/difficulties/all",
            "https://api.accsaber.com/v1/maps/difficulties/all",
            "https://accsaber.com/v1/maps/difficulties/all"
        };

        private static readonly string[] _mapByHashUrlPrefixes =
        {
            "https://accsaberreloaded.com/v1/maps/hash/",
            "https://accsaber.com/api/v1/maps/hash/",
            "https://api.accsaber.com/v1/maps/hash/",
            "https://accsaber.com/v1/maps/hash/"
        };

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

        private static readonly HashSet<string> _standardCharacteristics =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Standard",
                "SoloStandard"
            };

        // HashSet<"UPPERCASEHASH|DIFFICULTY_STRING"> for O(1) lookup.
        private volatile HashSet<string> _rankedSet = new HashSet<string>(StringComparer.Ordinal);
        private DateTime _lastFetchUtc = DateTime.MinValue;
        private int _fetchInFlight; // 0 = idle, 1 = fetching (interlocked flag)
        private string _lastSuccessfulBulkUrl;
        private string _lastSuccessfulMapByHashPrefix;

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

        internal async Task<bool> IsRankedAsync(string songHash, string difficultyLabel, string characteristic, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(songHash) || string.IsNullOrEmpty(difficultyLabel))
                return false;

            if (UsesStandardCharacteristic(characteristic))
            {
                await RefreshAsync().ConfigureAwait(false);

                if (IsRanked(songHash, difficultyLabel))
                    return true;
            }

            return await CheckSongHashAsync(songHash, difficultyLabel, characteristic, ct).ConfigureAwait(false);
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
            string[] urls = GetPreferredUrls(_bulkRankedListUrls, _lastSuccessfulBulkUrl);
            for (int index = 0; index < urls.Length; index++)
            {
                string url = urls[index];
                _log.Info("[BeatSurgeon][AccSaber] Fetching ranked list from " + url);

                try
                {
                    HttpResponseMessage response = await _http.GetAsync(url).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _log.Warn("[BeatSurgeon][AccSaber] Ranked list fetch failed at " + url + ": HTTP " + (int)response.StatusCode);
                        continue;
                    }

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var newSet = ParseRankedSet(json);
                    _rankedSet = newSet;
                    _lastFetchUtc = DateTime.UtcNow;
                    _lastSuccessfulBulkUrl = url;
                    _log.Info("[BeatSurgeon][AccSaber] Ranked list loaded from " + url + ": " + newSet.Count + " entries.");
                    return;
                }
                catch (Exception ex)
                {
                    _log.Warn("[BeatSurgeon][AccSaber] Ranked list fetch failed at " + url + ": " + ex.Message);
                }
            }

            _log.Warn("[BeatSurgeon][AccSaber] Ranked list fetch failed for all known endpoints. Keeping previous cache.");
        }

        private async Task<bool> CheckSongHashAsync(string songHash, string difficultyLabel, string characteristic, CancellationToken ct)
        {
            if (!_difficultyMap.TryGetValue(difficultyLabel, out string accSaberDiff))
                return false;

            string[] prefixes = GetPreferredUrls(_mapByHashUrlPrefixes, _lastSuccessfulMapByHashPrefix);
            for (int index = 0; index < prefixes.Length; index++)
            {
                string prefix = prefixes[index];
                string url = prefix + Uri.EscapeDataString(songHash.ToLowerInvariant());

                try
                {
                    HttpResponseMessage response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        _log.Debug("[BeatSurgeon][AccSaber] Map-by-hash endpoint returned 404 at " + url);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _log.Warn("[BeatSurgeon][AccSaber] Map-by-hash request failed at " + url + ": HTTP " + (int)response.StatusCode);
                        continue;
                    }

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    bool ranked = TryParseRankedMapResponse(json, accSaberDiff, characteristic, out string matchedCharacteristic);
                    _lastSuccessfulMapByHashPrefix = prefix;

                    _log.Debug("[BeatSurgeon][AccSaber] Map-by-hash lookup diff=" + accSaberDiff
                        + " characteristic=" + NormalizeCharacteristic(characteristic)
                        + " matchedCharacteristic=" + (string.IsNullOrEmpty(matchedCharacteristic) ? "<none>" : matchedCharacteristic)
                        + " ranked=" + ranked);

                    return ranked;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    _log.Warn("[BeatSurgeon][AccSaber] Map-by-hash request failed at " + url + ": " + ex.Message);
                }
            }

            _log.Warn("[BeatSurgeon][AccSaber] Map-by-hash fallback failed for all known endpoints for hash=" + songHash + " diff=" + accSaberDiff + ".");

            return false;
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

        private static bool TryParseRankedMapResponse(string json, string accSaberDiff, string characteristic, out string matchedCharacteristic)
        {
            matchedCharacteristic = null;
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                JObject root = JObject.Parse(json);
                JArray difficulties = root["difficulties"] as JArray;
                if (difficulties == null)
                    return false;

                string normalizedCharacteristic = NormalizeCharacteristic(characteristic);
                for (int index = 0; index < difficulties.Count; index++)
                {
                    JObject difficulty = difficulties[index] as JObject;
                    if (difficulty == null)
                        continue;

                    string apiDifficulty = difficulty["difficulty"]?.ToString() ?? string.Empty;
                    if (!string.Equals(apiDifficulty, accSaberDiff, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string apiCharacteristic = difficulty["characteristic"]?.ToString() ?? string.Empty;
                    if (!CharacteristicMatches(normalizedCharacteristic, apiCharacteristic))
                        continue;

                    matchedCharacteristic = apiCharacteristic;
                    string status = difficulty["status"]?.ToString() ?? string.Empty;
                    return string.Equals(status, "RANKED", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("[BeatSurgeon][AccSaber] Failed to parse map-by-hash response: " + ex.Message);
            }

            return false;
        }

        private static bool UsesStandardCharacteristic(string characteristic)
        {
            string normalized = NormalizeCharacteristic(characteristic);
            return string.IsNullOrEmpty(normalized) || _standardCharacteristics.Contains(normalized);
        }

        private static bool CharacteristicMatches(string expectedCharacteristic, string apiCharacteristic)
        {
            string normalizedApiCharacteristic = NormalizeCharacteristic(apiCharacteristic);
            if (string.IsNullOrEmpty(expectedCharacteristic))
                return UsesStandardCharacteristic(normalizedApiCharacteristic);

            return string.Equals(expectedCharacteristic, normalizedApiCharacteristic, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCharacteristic(string characteristic)
        {
            if (string.IsNullOrEmpty(characteristic))
                return string.Empty;

            if (string.Equals(characteristic, "SoloStandard", StringComparison.OrdinalIgnoreCase))
                return "Standard";

            return characteristic;
        }

        private static string[] GetPreferredUrls(string[] urls, string preferredUrl)
        {
            if (string.IsNullOrEmpty(preferredUrl))
                return urls;

            var ordered = new List<string>(urls.Length);
            ordered.Add(preferredUrl);

            for (int index = 0; index < urls.Length; index++)
            {
                string url = urls[index];
                if (!string.Equals(url, preferredUrl, StringComparison.OrdinalIgnoreCase))
                    ordered.Add(url);
            }

            return ordered.ToArray();
        }
    }
}
