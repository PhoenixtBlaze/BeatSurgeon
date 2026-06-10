using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Integration
{
    internal sealed class IntegrationWebSocketHost : IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("IntegrationWebSocketHost");
        private static readonly object AuthTokenLock = new object();

        private readonly IntegrationApiCoordinator _coordinator;
        private readonly object _listenerLock = new object();

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptTask;
        private bool _disposed;

        internal IntegrationWebSocketHost(IntegrationApiCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        internal bool IsRunning { get; private set; }

        internal void Start()
        {
            if (_disposed || IsRunning)
            {
                return;
            }

            PluginConfig config = PluginConfig.Instance;
            if (config == null || !config.IntegrationApiEnabled)
            {
                return;
            }

            EnsureAuthToken(config);

            int port = config.IntegrationApiPort > 0
                ? config.IntegrationApiPort
                : IntegrationApiConstants.DefaultPort;

            try
            {
                _listener = new TcpListener(IPAddress.Parse(IntegrationApiConstants.BindAddress), port);
                _listener.Start();
                _cts = new CancellationTokenSource();
                _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
                IsRunning = true;
                string tokenStatus = string.IsNullOrWhiteSpace(config.IntegrationApiAuthToken)
                    ? "missing"
                    : "configured";
                IntegrationApiLog.ServerStarted(port, tokenStatus);
                _log.Info("Integration API listening on ws://" + IntegrationApiConstants.BindAddress + ":" + port + IntegrationApiConstants.PathPrefix);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Start");
                IntegrationApiLog.ServerStopped("start failed: " + ex.Message);
                Stop();
            }
        }

        internal void Stop()
        {
            if (IsRunning)
            {
                IntegrationApiLog.ServerStopped("host stopped");
            }

            IsRunning = false;
            _cts?.Cancel();

            lock (_listenerLock)
            {
                try
                {
                    _listener?.Stop();
                }
                catch
                {
                }

                _listener = null;
            }

            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await Task.Run(() => _listener.AcceptTcpClient(), ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                    {
                        client.Close();
                        break;
                    }

                    if (!_coordinator.CanAcceptConnection())
                    {
                        string rejectedLabel = client.Client?.RemoteEndPoint?.ToString() ?? "client";
                        IntegrationApiLog.TransportHandshake(rejectedLabel, false, "client limit reached");
                        _log.Warn("Integration API rejected connection — client limit reached.");
                        client.Close();
                        continue;
                    }

                    _ = Task.Run(() => HandleClientAsync(client, ct), ct);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "AcceptLoopAsync");
                    client?.Close();
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            if (client == null)
            {
                return;
            }

            string clientLabel = client.Client?.RemoteEndPoint?.ToString() ?? "client";
            NetworkStream stream = null;
            IntegrationWebSocketConnection connection = null;

            try
            {
                stream = client.GetStream();
                PluginConfig config = PluginConfig.Instance;
                string token = config?.IntegrationApiAuthToken ?? string.Empty;
                bool handshakeOk = await IntegrationWebSocketFraming.TryAcceptHandshakeAsync(
                    stream,
                    IntegrationApiConstants.PathPrefix,
                    token,
                    ct).ConfigureAwait(false);

                if (!handshakeOk)
                {
                    IntegrationApiLog.TransportHandshake(clientLabel, false, "invalid upgrade, path, or token");
                    client.Close();
                    return;
                }

                IntegrationApiLog.TransportHandshake(clientLabel, true, "websocket upgrade complete");
                connection = new IntegrationWebSocketConnection(client, stream, _coordinator, clientLabel);
                if (!_coordinator.TryRegisterConnection(connection, clientLabel))
                {
                    connection.Dispose();
                    return;
                }

                connection.StartReceiveLoop();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "HandleClientAsync client=" + clientLabel);
                connection?.Dispose();
                if (connection == null)
                {
                    stream?.Dispose();
                    client.Close();
                }
            }
        }

        private static void EnsureAuthToken(PluginConfig config)
        {
            if (config == null)
            {
                return;
            }

            lock (AuthTokenLock)
            {
                if (!string.IsNullOrWhiteSpace(config.IntegrationApiAuthToken))
                {
                    MarkAuthTokenIssued(config);
                    return;
                }

                if (config.IntegrationApiAuthTokenIssued)
                {
                    IntegrationApiLog.AuthTokenMissingAfterIssued();
                    _log.Warn(
                        "Integration API auth token is missing but was previously issued. "
                        + "Restore IntegrationApiAuthToken in BeatSurgeon.json — a new token will not be auto-generated.");
                    return;
                }

                config.IntegrationApiAuthToken = Guid.NewGuid().ToString("N");
                config.IntegrationApiAuthTokenIssued = true;
                config.Changed();
                IntegrationApiLog.AuthTokenGenerated();
                _log.Info("Integration API auth token generated and saved to BeatSurgeon config.");
            }
        }

        private static void MarkAuthTokenIssued(PluginConfig config)
        {
            if (config.IntegrationApiAuthTokenIssued)
            {
                return;
            }

            config.IntegrationApiAuthTokenIssued = true;
            config.Changed();
        }
    }
}
