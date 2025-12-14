using System;
using System.Linq;
using System.Reflection;

namespace SaberSurgeon.Integrations
{
    internal static class BeatSaverDownloaderBridge
    {
        private static Assembly _bsdAsm;
        private static Type _songDownloaderType;
        private static object _songDownloaderInstance;
        private static MethodInfo _downloadMethod;

        public static bool IsAvailable
        {
            get { return Resolve(); }
        }

        public static bool TryDownloadByKey(string bsrKey, out string reason)
        {
            reason = null;

            if (!Resolve())
            {
                reason = "BeatSaverDownloader not found/loaded.";
                return false;
            }

            // Resolve download method once (C# 7.3-safe, no ??=)
            if (_downloadMethod == null)
            {
                var candidates = _songDownloaderType
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m =>
                    {
                        if (m == null) return false;
                        if (m.Name == null) return false;
                        if (!m.Name.ToLowerInvariant().Contains("download")) return false;

                        var ps = m.GetParameters();
                        return ps.Length == 1 && ps[0].ParameterType == typeof(string);
                    })
                    .ToArray();

                _downloadMethod = candidates.FirstOrDefault();
            }

            if (_downloadMethod == null)
            {
                reason = "BeatSaverDownloader download method not found (API mismatch).";
                return false;
            }

            try
            {
                var target = _downloadMethod.IsStatic ? null : _songDownloaderInstance;
                _downloadMethod.Invoke(target, new object[] { bsrKey });
                return true;
            }
            catch (Exception ex)
            {
                reason = "BeatSaverDownloader invoke failed: " + ex.GetType().Name;
                return false;
            }
        }

        private static bool Resolve()
        {
            if (_songDownloaderType != null)
                return true;

            if (_bsdAsm == null)
            {
                _bsdAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a =>
                        a != null &&
                        a.GetName() != null &&
                        a.GetName().Name != null &&
                        a.GetName().Name.IndexOf("BeatSaverDownloader", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (_bsdAsm == null)
                return false;

            if (_songDownloaderType == null)
            {
                _songDownloaderType = _bsdAsm.GetTypes()
                    .FirstOrDefault(t =>
                        t != null &&
                        t.Name != null &&
                        t.Name.IndexOf("SongDownloader", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (_songDownloaderType == null)
                return false;

            // Try to get singleton-ish Instance property if present
            var instProp = _songDownloaderType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            _songDownloaderInstance = instProp != null ? instProp.GetValue(null, null) : null;
            return true;
        }
    }
}
