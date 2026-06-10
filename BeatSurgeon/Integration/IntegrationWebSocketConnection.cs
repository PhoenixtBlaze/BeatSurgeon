using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;

namespace BeatSurgeon.Integration
{
    internal sealed class IntegrationWebSocketConnection : IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("IntegrationWebSocketConnection");

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly IntegrationApiCoordinator _coordinator;
        private readonly string _clientLabel;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _receiveTask;
        private bool _handshakeCompleted;
        private bool _disposed;

        internal IntegrationWebSocketConnection(
            TcpClient client,
            NetworkStream stream,
            IntegrationApiCoordinator coordinator,
            string clientLabel)
        {
            _client = client;
            _stream = stream;
            _coordinator = coordinator;
            _clientLabel = clientLabel ?? "client";
        }

        internal void StartReceiveLoop()
        {
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }

        internal async Task SendAsync(string message, CancellationToken ct)
        {
            if (_disposed || _stream == null || !_handshakeCompleted)
            {
                return;
            }

            await IntegrationWebSocketFraming.SendTextMessageAsync(_stream, message, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cts.Cancel();

            try
            {
                if (_stream != null && _handshakeCompleted)
                {
                    IntegrationWebSocketFraming.SendCloseAsync(_stream, CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch
            {
            }

            _stream?.Dispose();
            _client?.Close();
            _cts.Dispose();
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string message = await IntegrationWebSocketFraming
                        .ReceiveTextMessageAsync(_stream, ct)
                        .ConfigureAwait(false);

                    if (message == null)
                    {
                        IntegrationApiLog.Inbound(_clientLabel, "disconnect", string.Empty, "transport closed");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }

                    await HandleMessageAsync(message, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "ReceiveLoopAsync client=" + _clientLabel);
            }
            finally
            {
                _coordinator.RemoveConnection(this, _clientLabel);
            }
        }

        private async Task HandleMessageAsync(string rawJson, CancellationToken ct)
        {
            if (!_handshakeCompleted)
            {
                await HandlePreHandshakeAsync(rawJson, ct).ConfigureAwait(false);
                return;
            }

            if (!IntegrationApiProtocol.TryParseInbound(rawJson, out IntegrationInboundMessage inbound, out string parseError))
            {
                IntegrationApiLog.ProtocolError(_clientLabel, string.Empty, IntegrationRejectReason.InvalidMessage.ToString(), parseError);
                await SendErrorAsync(null, IntegrationRejectReason.InvalidMessage, parseError, ct).ConfigureAwait(false);
                return;
            }

            LogInboundMessage(inbound);
            switch (inbound.MessageType)
            {
                case IntegrationMessageType.Handshake:
                    await SendHandshakeAckAsync(inbound.Id, ct).ConfigureAwait(false);
                    break;
                case IntegrationMessageType.Ping:
                    await SendPongAsync(inbound.Id, ct).ConfigureAwait(false);
                    break;
                case IntegrationMessageType.CommandInvoke:
                    await HandleCommandInvokeAsync(inbound, ct).ConfigureAwait(false);
                    break;
                case IntegrationMessageType.EventRaise:
                    await HandleEventRaiseAsync(inbound, ct).ConfigureAwait(false);
                    break;
                default:
                    await SendErrorAsync(inbound.Id, IntegrationRejectReason.InvalidMessage, "Unsupported message type.", ct).ConfigureAwait(false);
                    break;
            }
        }

        private async Task HandlePreHandshakeAsync(string rawJson, CancellationToken ct)
        {
            if (!IntegrationApiProtocol.TryParseInbound(rawJson, out IntegrationInboundMessage inbound, out string parseError))
            {
                IntegrationApiLog.ProtocolError(_clientLabel, string.Empty, IntegrationRejectReason.InvalidMessage.ToString(), parseError);
                await SendErrorAsync(null, IntegrationRejectReason.InvalidMessage, parseError, ct).ConfigureAwait(false);
                return;
            }

            if (inbound.MessageType != IntegrationMessageType.Handshake)
            {
                IntegrationApiLog.ProtocolError(
                    _clientLabel,
                    inbound.Id,
                    IntegrationRejectReason.Unauthorized.ToString(),
                    "Handshake is required before other messages.");
                await SendErrorAsync(inbound.Id, IntegrationRejectReason.Unauthorized, "Handshake is required before other messages.", ct).ConfigureAwait(false);
                return;
            }

            LogInboundMessage(inbound);
            _handshakeCompleted = true;
            await SendHandshakeAckAsync(inbound.Id, ct).ConfigureAwait(false);
        }

        private async Task HandleCommandInvokeAsync(IntegrationInboundMessage inbound, CancellationToken ct)
        {
            IntegrationCommandResult result = await _coordinator.ExecuteCommandInvokeAsync(inbound, ct).ConfigureAwait(false);
            await SendCommandResultAsync(inbound.Id, result, ct).ConfigureAwait(false);
        }

        private async Task HandleEventRaiseAsync(IntegrationInboundMessage inbound, CancellationToken ct)
        {
            IntegrationCommandResult result = await _coordinator.ExecuteEventRaiseAsync(inbound, ct).ConfigureAwait(false);
            await SendEventResultAsync(inbound.Id, result, ct).ConfigureAwait(false);
        }

        private async Task SendHandshakeAckAsync(string correlationId, CancellationToken ct)
        {
            IntegrationHandshakeSnapshot snapshot = await _coordinator
                .BuildHandshakeSnapshotAsync(ct)
                .ConfigureAwait(false);

            var payload = new JObject
            {
                ["serverVersion"] = snapshot.ServerVersion,
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

            string response = IntegrationApiProtocol.BuildEnvelope("handshake.ack", null, payload, correlationId);
            IntegrationApiLog.HandshakeAck(
                correlationId,
                snapshot.HasVisualsAccess,
                snapshot.SupporterTier,
                snapshot.EntitlementProvider,
                snapshot.StandardCommands?.Length ?? 0,
                snapshot.SupporterCommands?.Length ?? 0);
            IntegrationApiLog.Outbound(
                _clientLabel,
                "handshake.ack",
                correlationId,
                "hasVisualsAccess=" + snapshot.HasVisualsAccess + " inMap=" + snapshot.InMap);
            await SendAsync(response, ct).ConfigureAwait(false);
        }

        private async Task SendCommandResultAsync(string correlationId, IntegrationCommandResult result, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["status"] = result.Accepted ? "accepted" : "rejected",
                ["commandKey"] = result.CommandKey ?? string.Empty,
                ["reason"] = result.Accepted ? "None" : result.Reason.ToString(),
                ["message"] = result.Message ?? string.Empty,
                ["refundRecommended"] = !result.Accepted && IntegrationApiProtocol.ShouldRecommendRefund(result.Reason)
            };

            if (result.CooldownRemaining.HasValue)
            {
                payload["cooldownRemainingSeconds"] = Math.Ceiling(result.CooldownRemaining.Value.TotalSeconds);
            }

            string response = IntegrationApiProtocol.BuildEnvelope("command.result", null, payload, correlationId);
            IntegrationApiLog.CommandResult(
                correlationId,
                result.Accepted,
                result.CommandKey,
                result.Reason.ToString(),
                result.Message);
            IntegrationApiLog.Outbound(
                _clientLabel,
                "command.result",
                correlationId,
                "status=" + (result.Accepted ? "accepted" : "rejected") + " reason=" + result.Reason);
            await SendAsync(response, ct).ConfigureAwait(false);
        }

        private async Task SendEventResultAsync(string correlationId, IntegrationCommandResult result, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["status"] = result.Accepted ? "accepted" : "rejected",
                ["eventKey"] = result.CommandKey ?? string.Empty,
                ["reason"] = result.Accepted ? "None" : result.Reason.ToString(),
                ["message"] = result.Message ?? string.Empty
            };

            string response = IntegrationApiProtocol.BuildEnvelope("event.result", null, payload, correlationId);
            IntegrationApiLog.EventResult(
                correlationId,
                result.Accepted,
                result.CommandKey,
                result.Reason.ToString(),
                result.Message);
            IntegrationApiLog.Outbound(
                _clientLabel,
                "event.result",
                correlationId,
                "status=" + (result.Accepted ? "accepted" : "rejected") + " reason=" + result.Reason);
            await SendAsync(response, ct).ConfigureAwait(false);
        }

        private async Task SendPongAsync(string correlationId, CancellationToken ct)
        {
            string response = IntegrationApiProtocol.BuildEnvelope("pong", null, new JObject(), correlationId);
            IntegrationApiLog.Outbound(_clientLabel, "pong", correlationId, string.Empty);
            await SendAsync(response, ct).ConfigureAwait(false);
        }

        private async Task SendErrorAsync(string correlationId, IntegrationRejectReason reason, string message, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["status"] = "rejected",
                ["reason"] = reason.ToString(),
                ["message"] = message ?? string.Empty
            };

            string response = IntegrationApiProtocol.BuildEnvelope("error", null, payload, correlationId);
            IntegrationApiLog.ProtocolError(_clientLabel, correlationId, reason.ToString(), message);
            IntegrationApiLog.Outbound(_clientLabel, "error", correlationId, "reason=" + reason);
            await SendAsync(response, ct).ConfigureAwait(false);
        }

        private void LogInboundMessage(IntegrationInboundMessage inbound)
        {
            if (inbound == null)
            {
                return;
            }

            string type = inbound.MessageType.ToString();
            string summary = BuildInboundSummary(inbound);
            IntegrationApiLog.Inbound(_clientLabel, type, inbound.Id ?? string.Empty, summary);
        }

        private static string BuildInboundSummary(IntegrationInboundMessage inbound)
        {
            JObject payload = inbound.Payload ?? new JObject();
            switch (inbound.MessageType)
            {
                case IntegrationMessageType.Handshake:
                    return "client=" + (payload.Value<string>("client") ?? "unknown")
                        + " version=" + (payload.Value<string>("clientVersion") ?? "unknown");
                case IntegrationMessageType.CommandInvoke:
                    return "user=" + GetViewerName(payload["viewer"])
                        + " command=" + (payload.Value<string>("command") ?? string.Empty);
                case IntegrationMessageType.EventRaise:
                    return "name=" + (payload.Value<string>("name") ?? string.Empty)
                        + " user=" + GetViewerName(payload["data"]?["viewer"] ?? payload["data"]?["buyer"]);
                case IntegrationMessageType.Ping:
                    return "keepalive";
                default:
                    return string.Empty;
            }
        }

        private static string GetViewerName(JToken viewerToken)
        {
            if (viewerToken == null)
            {
                return "Unknown";
            }

            string name = viewerToken["name"]?.ToString();
            return string.IsNullOrWhiteSpace(name) ? "Unknown" : name.Trim();
        }
    }
}
