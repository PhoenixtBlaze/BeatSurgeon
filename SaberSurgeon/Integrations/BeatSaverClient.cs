using System.Text.RegularExpressions;
using UnityEngine.Networking;

namespace SaberSurgeon.Integrations
{
    internal static class BeatSaverClient
    {
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
