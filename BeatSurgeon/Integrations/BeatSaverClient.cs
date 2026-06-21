using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace BeatSurgeon.Integrations
{
    internal static class BeatSaverClient
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // GameplayManager already uses this style (regex, no JSON dependency) [file:29]
        private static readonly Regex HashRegex =
            new Regex("\"hash\"\\s*:\\s*\"([0-9a-fA-F]{40})\"", RegexOptions.Compiled);

        private static readonly Regex DownloadUrlRegex =
            new Regex("\"downloadURL\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        private static readonly Regex SongNameRegex =
            new Regex("\"songName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        private static readonly Regex LevelAuthorRegex =
            new Regex("\"levelAuthorName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        internal static UnityWebRequest GetMapMetadata(string key)
            => UnityWebRequest.Get($"https://api.beatsaver.com/maps/id/{key}");

        internal static async Task<string> ResolveMapHashAsync(string key, CancellationToken ct)
        {
            string normalizedKey = (key ?? string.Empty).Trim().TrimStart('!');
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return null;
            }

            try
            {
                using (var response = await _http.GetAsync($"https://api.beatsaver.com/maps/id/{normalizedKey}", ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Match hm = HashRegex.Match(json);
                    if (!hm.Success)
                    {
                        return null;
                    }

                    return hm.Groups[1].Value.ToLowerInvariant();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryParse(string json, out string hashLower, out string downloadUrl, out string songName, out string levelAuthor)
        {
            hashLower = null; downloadUrl = null; songName = null; levelAuthor = null;
            if (string.IsNullOrEmpty(json)) return false;

            var hm = HashRegex.Match(json);
            var dm = DownloadUrlRegex.Match(json);
            if (!hm.Success || !dm.Success) return false;

            hashLower = hm.Groups[1].Value.ToLowerInvariant();
            downloadUrl = dm.Groups[1].Value.Replace("\\/", "/");

            var sn = SongNameRegex.Match(json);
            if (sn.Success) songName = sn.Groups[1].Value;

            var la = LevelAuthorRegex.Match(json);
            if (la.Success) levelAuthor = la.Groups[1].Value;

            return true;
        }
    }
}
