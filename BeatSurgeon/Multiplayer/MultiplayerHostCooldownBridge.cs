using System;
using System.Collections.Generic;
using BeatSurgeon.Chat;

namespace BeatSurgeon
{
    /// <summary>
    /// Client-side cache of the Multiplayer+ host's cooldown configuration.
    ///
    /// While a client is in a room (not hosting), effects are applied from host-synced one-shots
    /// rather than local chat/CP triggers, so the client must use the HOST's cooldown values
    /// (not its own PluginConfig) for CommandCooldownService bookkeeping. Solo play and hosting
    /// always use local PluginConfig cooldowns untouched.
    /// </summary>
    internal static class MultiplayerHostCooldownBridge
    {
        private static readonly object _lock = new object();
        private static bool _perCommandCooldownsEnabled = true;
        private static Dictionary<string, double> _cooldowns = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static bool _hasHostData;
        private static HashSet<string> _enabledCommands;
        private static bool? _showUsernameInBmsg;

        internal static bool HasHostData
        {
            get { lock (_lock) return _hasHostData; }
        }

        /// <summary>
        /// True when host-provided cooldown values should be preferred over local PluginConfig
        /// (in a Multiplayer+ room, not hosting, and host has sent at least one cooldown snapshot).
        /// </summary>
        internal static bool ShouldUseHostValues =>
            _hasHostData && SceneHelper.MpPlusInRoom && !SceneHelper.MpPlusIsHost;

        internal static bool PerCommandCooldownsEnabled
        {
            get { lock (_lock) return _perCommandCooldownsEnabled; }
        }

        internal static bool TryGetCooldownSeconds(string commandKey, out double seconds)
        {
            lock (_lock)
            {
                return _cooldowns.TryGetValue(commandKey ?? string.Empty, out seconds);
            }
        }

        internal static bool IsBlockedByHostToggle(string commandKey)
        {
            string key = (commandKey ?? string.Empty).Trim().ToLowerInvariant();
            lock (_lock)
            {
                if (_enabledCommands == null || key.Length == 0)
                {
                    return false;
                }

                return !_enabledCommands.Contains(key);
            }
        }

        /// <summary>
        /// Room-synced !bmsg uses the host toggle when this client has received it.
        /// The host, solo play, and an older snapshot fall back to the local setting.
        /// </summary>
        internal static bool ShouldShowUsernameInBmsg(TriggerSource source)
        {
            if (source == TriggerSource.MultiplayerSync && ShouldUseHostValues)
            {
                lock (_lock)
                {
                    if (_showUsernameInBmsg.HasValue)
                    {
                        return _showUsernameInBmsg.Value;
                    }
                }
            }

            return PluginConfig.Instance?.ShowUsernameInBmsg ?? true;
        }

        internal static void ApplyFromHost(bool perCommandEnabled, Dictionary<string, double> hostCooldowns, IList<string> enabledCommands, bool? showUsernameInBmsg = null)
        {
            lock (_lock)
            {
                _perCommandCooldownsEnabled = perCommandEnabled;
                _cooldowns = hostCooldowns != null
                    ? new Dictionary<string, double>(hostCooldowns, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                _hasHostData = true;
                _showUsernameInBmsg = showUsernameInBmsg;
                if (enabledCommands == null)
                {
                    _enabledCommands = null;
                }
                else
                {
                    _enabledCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < enabledCommands.Count; i++)
                    {
                        string key = enabledCommands[i];
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            _enabledCommands.Add(key.Trim());
                        }
                    }
                }
            }
        }

        /// <summary>Call on leaving a Multiplayer+ room so a future solo/host session isn't affected.</summary>
        internal static void Clear()
        {
            lock (_lock)
            {
                _perCommandCooldownsEnabled = true;
                _cooldowns.Clear();
                _enabledCommands = null;
                _showUsernameInBmsg = null;
                _hasHostData = false;
            }
        }
    }
}
