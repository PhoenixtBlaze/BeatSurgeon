using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SaberSurgeon.Integrations
{
    internal static class SongInstaller
    {
        internal static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Unknown";
            var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct().ToArray();
            foreach (var c in invalid) s = s.Replace(c, '_');
            return s.Trim();
        }

        internal static void SafeExtractZip(byte[] zipBytes, string destDir)
        {
            Directory.CreateDirectory(destDir);

            var ms = new MemoryStream(zipBytes);
            var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            var destFull = Path.GetFullPath(destDir);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // folder entry

                var outPath = Path.Combine(destDir, entry.FullName);
                var outFull = Path.GetFullPath(outPath);

                
                if (!outFull.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(outFull));
                using (var inStream = entry.Open())
                using (var outStream = new FileStream(outFull, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    inStream.CopyTo(outStream);
                }

            }
        }
    }
}
