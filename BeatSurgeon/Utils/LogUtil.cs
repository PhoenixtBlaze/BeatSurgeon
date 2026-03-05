using System;
using System.Runtime.CompilerServices;
using IPA.Logging;

namespace BeatSurgeon.Utils
{
    /// <summary>
    /// Central logging utility for BeatSurgeon with scoped child loggers.
    /// </summary>
    internal sealed class LogUtil
    {
        internal enum Level
        {
            Debug,
            Info,
            Notice,
            Warn,
            Error,
            Critical
        }

        private readonly Logger _ipaLogger;
        private readonly string _scope;

        private LogUtil(Logger ipaLogger, string scope)
        {
            _ipaLogger = ipaLogger;
            _scope = scope;
        }

        internal static LogUtil Root { get; private set; }

        internal static void Initialize(Logger ipaLogger)
        {
            Root = new LogUtil(ipaLogger, "BeatSurgeon");
            Root.Info("LogUtil initialized. BeatSurgeon logging online.");
        }

        internal static LogUtil GetLogger(string scope)
        {
            if (Root == null)
            {
                throw new InvalidOperationException(
                    "LogUtil.Initialize() must be called before GetLogger().");
            }

            return new LogUtil(Root._ipaLogger, string.IsNullOrWhiteSpace(scope) ? "UnknownScope" : scope);
        }

        internal void Debug(
            string message,
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Debug, message, caller, line);

        internal void Info(
            string message,
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Info, message, caller, line);

        internal void Notice(
            string message,
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Notice, message, caller, line);

        internal void Warn(
            string message,
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Warn, message, caller, line);

        internal void Error(
            string message,
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Error, message, caller, line);

        internal void Critical(
            string message,
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Critical, message, caller, line);

        internal void Exception(
            Exception ex,
            string context = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
        {
            if (ex == null)
            {
                Write(Level.Error, $"EXCEPTION in {context}: <null>", caller, line);
                return;
            }

            Write(
                Level.Error,
                $"EXCEPTION in {context}: [{ex.GetType().Name}] {ex.Message}{Environment.NewLine}{ex}",
                caller,
                line);
        }

        internal void CriticalException(
            Exception ex,
            string context = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
        {
            if (ex == null)
            {
                Write(Level.Critical, $"CRITICAL EXCEPTION in {context}: <null>", caller, line);
                return;
            }

            Write(
                Level.Critical,
                $"CRITICAL EXCEPTION in {context}: [{ex.GetType().Name}] {ex.Message}{Environment.NewLine}{ex}",
                caller,
                line);
        }

        internal void Lifecycle(
            string phase,
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Info, $"[LIFECYCLE] {phase}", caller, line);

        internal void TwitchState(
            string state,
            string detail = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Notice, "[TWITCH-STATE] " + state + Detail(detail), caller, line);

        internal void ChannelPoint(
            string rewardId,
            string eventType,
            string detail = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Info, $"[CP:{rewardId}] {eventType}{Detail(detail)}", caller, line);

        internal void Command(
            string user,
            string command,
            bool accepted,
            string reason = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Debug, $"[CMD] User={user} Cmd={command} Accepted={accepted}{Reason(reason)}", caller, line);

        internal void Effect(
            string effectName,
            bool applied,
            string detail = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Info, $"[EFFECT] {effectName} Applied={applied}{Detail(detail)}", caller, line);

        internal void MultiplayerSync(
            string eventType,
            string detail = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Debug, $"[MP-SYNC] {eventType}{Detail(detail)}", caller, line);

        internal void EventSub(
            string rewardId,
            string eventType,
            string detail = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Info, $"[EVENTSUB:{rewardId}] {eventType}{Detail(detail)}", caller, line);

        internal void Auth(
            string phase,
            string detail = "",
            [CallerMemberName] string caller = "",
            [CallerLineNumber] int line = 0)
            => Write(Level.Notice, $"[AUTH] {phase}{Detail(detail)}", caller, line);

        private static string Detail(string value)
            => string.IsNullOrWhiteSpace(value) ? string.Empty : " | " + value;

        private static string Reason(string value)
            => string.IsNullOrWhiteSpace(value) ? string.Empty : " Reason=" + value;

        private void Write(Level level, string message, string caller, int line)
        {
            string formatted = $"[{_scope}::{caller}:{line}] {message}";
            switch (level)
            {
                case Level.Debug:
                    _ipaLogger.Debug(formatted);
                    break;
                case Level.Info:
                    _ipaLogger.Info(formatted);
                    break;
                case Level.Notice:
                    _ipaLogger.Notice(formatted);
                    break;
                case Level.Warn:
                    _ipaLogger.Warn(formatted);
                    break;
                case Level.Error:
                    _ipaLogger.Error(formatted);
                    break;
                case Level.Critical:
                    _ipaLogger.Critical(formatted);
                    break;
            }
        }
    }
}
