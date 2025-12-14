using System;

namespace SaberSurgeon.Gameplay
{
    public class SongRequest
    {
        public string BsrCode { get; set; }
        public string RequesterName { get; set; }
        public DateTime RequestTime { get; set; }

        // Optional request fields (used later by jumpcut switching).
        public BeatmapDifficulty? RequestedDifficulty { get; set; }
        public float? StartTimeSeconds { get; set; }     // start offset into the song
        public float? SegmentLengthSeconds { get; set; } // how long to play from StartTimeSeconds
        public float? SwitchAfterSeconds { get; set; }  // e.g., 60 means "switch in 60s of current song"

    }
}
