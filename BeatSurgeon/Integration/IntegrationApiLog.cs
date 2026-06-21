using BeatSurgeon.Utils;

namespace BeatSurgeon.Integration
{
    internal static class IntegrationApiLog
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("IntegrationApi");

        internal static void ServerStarted(int port, string tokenConfigured)
        {
            _log.IntegrationApi(
                "ServerStarted",
                "port=" + port + " tokenConfigured=" + tokenConfigured + " active=false (waiting for client)");
        }

        internal static void ServerActive(int connectedClients, int maxClients)
        {
            _log.IntegrationApi(
                "ServerActive",
                "connectedClients=" + connectedClients + "/" + maxClients);
        }

        internal static void ServerIdle()
        {
            _log.IntegrationApi("ServerIdle", "no clients connected; broadcasts suspended");
        }

        internal static void ServerStopped(string reason)
        {
            _log.IntegrationApi("ServerStopped", reason);
        }

        internal static void ServerDisabled(string reason)
        {
            _log.IntegrationApi("ServerDisabled", reason);
        }

        internal static void AuthTokenGenerated()
        {
            _log.IntegrationApi(
                "AuthTokenGenerated",
                "token saved to UserData/BeatSurgeon.json IntegrationApiAuthToken (stable — will not auto-regenerate)");
        }

        internal static void AuthTokenMissingAfterIssued()
        {
            _log.IntegrationApi(
                "AuthTokenMissingAfterIssued",
                "IntegrationApiAuthToken is empty but was previously issued — restore the token in BeatSurgeon.json and Streamer.bot (no auto-regeneration)");
        }

        internal static void TransportHandshake(string clientLabel, bool accepted, string detail = "")
        {
            _log.IntegrationApi(
                accepted ? "TransportHandshakeAccepted" : "TransportHandshakeRejected",
                "client=" + clientLabel + Detail(detail));
        }

        internal static void ClientConnected(string clientLabel, int connectedClients, int maxClients)
        {
            _log.IntegrationApi(
                "ClientConnected",
                "client=" + clientLabel + " connectedClients=" + connectedClients + "/" + maxClients);
        }

        internal static void ClientDisconnected(string clientLabel, int connectedClients)
        {
            _log.IntegrationApi(
                "ClientDisconnected",
                "client=" + clientLabel + " connectedClients=" + connectedClients);
        }

        internal static void Inbound(string clientLabel, string messageType, string messageId, string summary)
        {
            _log.IntegrationApi(
                "Inbound",
                "client=" + clientLabel + " type=" + messageType + " id=" + messageId + " " + summary);
        }

        internal static void Outbound(string clientLabel, string messageType, string correlationId, string summary)
        {
            _log.IntegrationApi(
                "Outbound",
                "client=" + clientLabel + " type=" + messageType + " correlationId=" + correlationId + " " + summary);
        }

        internal static void CommandInvoke(string messageId, string user, string command)
        {
            _log.IntegrationApi("CommandInvoke", "id=" + messageId + " user=" + user + " command=" + command);
        }

        internal static void CommandResult(string messageId, bool accepted, string commandKey, string reason, string message)
        {
            _log.IntegrationApi(
                "CommandResult",
                "id=" + messageId
                + " accepted=" + accepted
                + " commandKey=" + commandKey
                + " reason=" + reason
                + " message=" + message);
        }

        internal static void EventRaise(string messageId, string eventName, string user, string summary)
        {
            _log.IntegrationApi(
                "EventRaise",
                "id=" + messageId + " name=" + eventName + " user=" + user + " " + summary);
        }

        internal static void EventResult(string messageId, bool accepted, string eventKey, string reason, string message)
        {
            _log.IntegrationApi(
                "EventResult",
                "id=" + messageId
                + " accepted=" + accepted
                + " eventKey=" + eventKey
                + " reason=" + reason
                + " message=" + message);
        }

        internal static void StateChanged(bool inMap, bool rankedBlocked, bool hasVisualsAccess, int supporterTier)
        {
            _log.IntegrationApi(
                "StateChanged",
                "inMap=" + inMap
                + " rankedBlocked=" + rankedBlocked
                + " hasVisualsAccess=" + hasVisualsAccess
                + " supporterTier=" + supporterTier);
        }

        internal static void HandshakeAck(
            string messageId,
            bool hasVisualsAccess,
            int supporterTier,
            string provider,
            int standardCommandCount,
            int supporterCommandCount)
        {
            _log.IntegrationApi(
                "HandshakeAck",
                "id=" + messageId
                + " hasVisualsAccess=" + hasVisualsAccess
                + " tier=" + supporterTier
                + " provider=" + provider
                + " standardCommands=" + standardCommandCount
                + " supporterCommands=" + supporterCommandCount);
        }

        internal static void DedupBlocked(string kind, string dedupKey, string source)
        {
            _log.IntegrationApi("DedupBlocked", "kind=" + kind + " key=" + dedupKey + " source=" + source);
        }

        internal static void ProtocolError(string clientLabel, string messageId, string reason, string message)
        {
            _log.IntegrationApi(
                "ProtocolError",
                "client=" + clientLabel + " id=" + messageId + " reason=" + reason + " message=" + message);
        }

        private static string Detail(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : " | " + value;
        }
    }
}
