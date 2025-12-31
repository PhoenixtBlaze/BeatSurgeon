using System;

namespace SaberSurgeon
{
    public static class LogUtils
    {
        // Only log if DebugMode is enabled in Config
        public static void Debug(string message)
        {
            if (PluginConfig.Instance.DebugMode)
            {
                Plugin.Log.Info($"[DEBUG] {message}");
            }
        }

        // Always log Warnings/Errors regardless of DebugMode
        public static void Warn(string message) => Plugin.Log.Warn(message);
        public static void Error(string message) => Plugin.Log.Error(message);

        // Use for critical startup/state changes that should always be visible
        public static void Info(string message) => Plugin.Log.Info(message);
    }
}
