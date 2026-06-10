using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BeatSurgeon.Integration
{
    internal sealed class IntegrationApiCoordinator : IInitializable, IDisposable, ITickable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("IntegrationApiCoordinator");
        private static readonly TimeSpan StateBroadcastInterval = TimeSpan.FromSeconds(2);

        private readonly IntegrationCommandExecutor _commandExecutor;
        private readonly IntegrationEventExecutor _eventExecutor;
        private readonly IntegrationWebSocketHost _host;
        private readonly object _connectionLock = new object();
        private readonly List<IntegrationWebSocketConnection> _connections = new List<IntegrationWebSocketConnection>();

        private IntegrationHandshakeSnapshot _lastBroadcastSnapshot;
        private DateTime _nextStateBroadcastUtc = DateTime.MinValue;
        private bool _disposed;
        private bool _disabledLogged;

        [Inject]
        public IntegrationApiCoordinator(
            IntegrationCommandExecutor commandExecutor,
            IntegrationEventExecutor eventExecutor)
        {
            _commandExecutor = commandExecutor;
            _eventExecutor = eventExecutor;
            _host = new IntegrationWebSocketHost(this);
        }

        internal int ConnectedClientCount
        {
            get
            {
                lock (_connectionLock)
                {
                    return _connections.Count;
                }
            }
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize");
            EntitlementsState.Changed += OnEntitlementsChanged;
            RestartIfNeeded();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            EntitlementsState.Changed -= OnEntitlementsChanged;
            _host.Dispose();

            lock (_connectionLock)
            {
                for (int i = _connections.Count - 1; i >= 0; i--)
                {
                    _connections[i]?.Dispose();
                }

                _connections.Clear();
            }
        }

        public void Tick()
        {
            if (_disposed)
            {
                return;
            }

            RestartIfNeeded();

            if (!_host.IsRunning || ConnectedClientCount == 0)
            {
                return;
            }

            if (DateTime.UtcNow < _nextStateBroadcastUtc)
            {
                return;
            }

            _nextStateBroadcastUtc = DateTime.UtcNow + StateBroadcastInterval;
            IntegrationHandshakeSnapshot current = IntegrationCapabilitiesBuilder.BuildSnapshot();
            current.ConnectedClients = ConnectedClientCount;

            if (ShouldBroadcastStateChange(current))
            {
                _lastBroadcastSnapshot = current;
                BroadcastStateChanged(current);
            }
        }

        internal bool CanAcceptConnection()
        {
            lock (_connectionLock)
            {
                return _connections.Count < IntegrationApiConstants.MaxClients;
            }
        }

        internal bool TryRegisterConnection(IntegrationWebSocketConnection connection, string clientLabel)
        {
            if (connection == null)
            {
                return false;
            }

            lock (_connectionLock)
            {
                if (_connections.Count >= IntegrationApiConstants.MaxClients)
                {
                    return false;
                }

                _connections.Add(connection);
            }

            IntegrationApiLog.ClientConnected(
                clientLabel ?? "client",
                ConnectedClientCount,
                IntegrationApiConstants.MaxClients);
            IntegrationApiLog.ServerActive(ConnectedClientCount, IntegrationApiConstants.MaxClients);
            _log.Info("Integration API client connected count=" + ConnectedClientCount);
            BroadcastStateChanged(IntegrationCapabilitiesBuilder.BuildSnapshot());
            return true;
        }

        internal void RemoveConnection(IntegrationWebSocketConnection connection, string clientLabel = "client")
        {
            if (connection == null)
            {
                return;
            }

            bool removed;
            lock (_connectionLock)
            {
                removed = _connections.Remove(connection);
            }

            if (!removed)
            {
                return;
            }

            connection.Dispose();
            IntegrationApiLog.ClientDisconnected(clientLabel ?? "client", ConnectedClientCount);
            _log.Info("Integration API client disconnected count=" + ConnectedClientCount);
            if (ConnectedClientCount == 0)
            {
                IntegrationApiLog.ServerIdle();
                _lastBroadcastSnapshot = null;
            }
            else
            {
                BroadcastStateChanged(IntegrationCapabilitiesBuilder.BuildSnapshot());
            }
        }

        internal async Task<IntegrationHandshakeSnapshot> BuildHandshakeSnapshotAsync(CancellationToken ct)
        {
            try
            {
                await PremiumVisualFeatureAccessController.RefreshVisualsPermissionAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("Handshake entitlement refresh failed: " + ex.Message);
            }

            IntegrationHandshakeSnapshot snapshot = IntegrationCapabilitiesBuilder.BuildSnapshot();
            snapshot.ConnectedClients = ConnectedClientCount;
            return snapshot;
        }

        internal Task<IntegrationCommandResult> ExecuteCommandInvokeAsync(
            IntegrationInboundMessage inbound,
            CancellationToken ct)
        {
            return _commandExecutor.ExecuteInvokeAsync(inbound, ct);
        }

        internal Task<IntegrationCommandResult> ExecuteEventRaiseAsync(
            IntegrationInboundMessage inbound,
            CancellationToken ct)
        {
            return _eventExecutor.ExecuteRaiseAsync(inbound, ct);
        }

        private void RestartIfNeeded()
        {
            PluginConfig config = PluginConfig.Instance;
            bool shouldRun = config != null && config.IntegrationApiEnabled;
            if (shouldRun && !_host.IsRunning)
            {
                _disabledLogged = false;
                _host.Start();
                _lastBroadcastSnapshot = IntegrationCapabilitiesBuilder.BuildSnapshot();
            }
            else if (!shouldRun)
            {
                if (_host.IsRunning)
                {
                    _host.Stop();
                    lock (_connectionLock)
                    {
                        for (int i = _connections.Count - 1; i >= 0; i--)
                        {
                            _connections[i]?.Dispose();
                        }

                        _connections.Clear();
                    }
                }
                else if (!_disabledLogged)
                {
                    string reason = config == null
                        ? "config unavailable"
                        : "IntegrationApiEnabled=false (set true in UserData/BeatSurgeon.json)";
                    IntegrationApiLog.ServerDisabled(reason);
                    _disabledLogged = true;
                }
            }
        }

        private void OnEntitlementsChanged()
        {
            if (!_host.IsRunning || ConnectedClientCount == 0)
            {
                return;
            }

            IntegrationHandshakeSnapshot snapshot = IntegrationCapabilitiesBuilder.BuildSnapshot();
            _lastBroadcastSnapshot = snapshot;
            BroadcastStateChanged(snapshot);
        }

        private bool ShouldBroadcastStateChange(IntegrationHandshakeSnapshot current)
        {
            if (_lastBroadcastSnapshot == null)
            {
                return true;
            }

            return _lastBroadcastSnapshot.HasVisualsAccess != current.HasVisualsAccess
                || _lastBroadcastSnapshot.SupporterTier != current.SupporterTier
                || _lastBroadcastSnapshot.InMap != current.InMap
                || _lastBroadcastSnapshot.RankedBlocked != current.RankedBlocked
                || _lastBroadcastSnapshot.GlobalDisabled != current.GlobalDisabled
                || !string.Equals(_lastBroadcastSnapshot.EntitlementProvider, current.EntitlementProvider, StringComparison.Ordinal);
        }

        private void BroadcastStateChanged(IntegrationHandshakeSnapshot snapshot)
        {
            if (ConnectedClientCount == 0)
            {
                return;
            }

            snapshot.ConnectedClients = ConnectedClientCount;
            var payload = new JObject
            {
                ["entitlements"] = new JObject
                {
                    ["hasVisualsAccess"] = snapshot.HasVisualsAccess,
                    ["tier"] = snapshot.SupporterTier,
                    ["provider"] = snapshot.EntitlementProvider
                },
                ["gameState"] = new JObject
                {
                    ["inMap"] = snapshot.InMap,
                    ["rankedBlocked"] = snapshot.RankedBlocked,
                    ["globalDisabled"] = snapshot.GlobalDisabled
                },
                ["capabilities"] = new JObject
                {
                    ["standardCommands"] = new JArray(snapshot.StandardCommands),
                    ["supporterCommands"] = new JArray(snapshot.SupporterCommands)
                },
                ["connection"] = new JObject
                {
                    ["connectedClients"] = snapshot.ConnectedClients,
                    ["maxClients"] = snapshot.MaxClients
                }
            };

            string message = IntegrationApiProtocol.BuildEnvelope("state.changed", null, payload);
            IntegrationApiLog.StateChanged(
                snapshot.InMap,
                snapshot.RankedBlocked,
                snapshot.HasVisualsAccess,
                snapshot.SupporterTier);
            BroadcastToAll(message);
        }

        private void BroadcastToAll(string message)
        {
            IntegrationWebSocketConnection[] snapshot;
            lock (_connectionLock)
            {
                snapshot = _connections.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].SendAsync(message, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _log.Warn("Broadcast failed: " + ex.Message);
                }
            }
        }
    }
}
