using System;
using BeatSurgeon.Utils;

namespace BeatSurgeon
{
    public static class LogUtils
    {
        public static bool DebugEnabled => PluginConfig.Instance != null && PluginConfig.Instance.DebugMode;

        public static void Debug(Func<string> messageFactory)
        {
            if (!DebugEnabled || messageFactory == null) return;
            Log("Legacy", l => l.Debug(messageFactory()), () => Plugin.Log?.Info("[DEBUG] " + messageFactory()));
        }

        public static void Warn(string message)
            => Log("Legacy", l => l.Warn(message), () => Plugin.Log?.Warn(message));

        public static void Error(string message)
            => Log("Legacy", l => l.Error(message), () => Plugin.Log?.Error(message));

        public static void Info(string message)
            => Log("Legacy", l => l.Info(message), () => Plugin.Log?.Info(message));

        private static void Log(string scope, Action<LogUtil> withScopedLogger, Action fallback)
        {
            try
            {
                withScopedLogger?.Invoke(LogUtil.GetLogger(scope));
            }
            catch
            {
                fallback?.Invoke();
            }
        }
    }
}
