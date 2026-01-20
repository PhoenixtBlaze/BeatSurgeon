using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BeatSaberPlaylistsLib.Types;
using PlaylistManager.Utilities;

namespace BeatSurgeon.Integrations
{
    internal static class EndlessPlaylistService
    {
        internal const string PlaylistName = "Endless Mode";
        private const string CoverResourcePath = "BeatSurgeon.Assets.Endless.png";

        internal static IPlaylist GetOrCreate()
        {
            string LoadCoverAsBase64()
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(CoverResourcePath))
                {
                    if (s == null) return null;

                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return System.Convert.ToBase64String(ms.ToArray());
                    }
                }
            }

            var pm = PlaylistLibUtils.playlistManager;
            if (pm == null) return null;

            // 1. Try to find existing playlist by Title
            var all = pm.GetAllPlaylists(includeChildren: true, out var _);
            var existing = all?.FirstOrDefault(p => p != null && p.Title == PlaylistName);

            if (existing != null)
            {
                // Ensure filename matches what we expect, just in case
                if (string.IsNullOrEmpty(existing.Filename))
                {
                    existing.Filename = PlaylistName;
                }
                return existing;
            }

            // 2. Create new if not found
            var coverBase64 = LoadCoverAsBase64() ?? "";

            // CreatePlaylist automatically instantiates the correct IPlaylist implementation
            var playlist = pm.CreatePlaylist(
                PlaylistName,     // fileName
                PlaylistName,     // title
                "BeatSurgeon",   // author
                coverBase64,      // coverImage
                "Queue for BeatSurgeon Endless Mode" // description
            );

            if (playlist != null)
            {
                playlist.AllowDuplicates = false;
                pm.StorePlaylist(playlist); // Save it to disk immediately so it's "real"
            }

            return playlist;
        }


        internal static void AddLevel(IPlaylist playlist, BeatmapLevel level)
        {
            if (playlist == null || level == null) return;

            try
            {
                bool alreadyExists = playlist.Any(x => x != null && x.LevelId == level.levelID);
                if (alreadyExists) return;

                var song = playlist.Add(level);
                playlist.RaisePlaylistChanged();

                try
                {
                    var pm = PlaylistLibUtils.playlistManager;
                    if (pm != null)
                        pm.StorePlaylist(playlist);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warn($"EndlessPlaylistService: StorePlaylist failed (non-fatal): {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"EndlessPlaylistService: Failed to add song '{level.songName}' to playlist: {ex.Message}");
            }
        }

    }
}
