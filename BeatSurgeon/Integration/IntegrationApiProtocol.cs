using System;
using System.Collections.Generic;
using BeatSurgeon.Chat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeatSurgeon.Integration
{
    internal enum IntegrationMessageType
    {
        Unknown = 0,
        Handshake,
        CommandInvoke,
        EventRaise,
        Ping
    }

    internal enum IntegrationResultStatus
    {
        Accepted,
        Rejected
    }

    internal enum IntegrationRejectReason
    {
        None,
        Unauthorized,
        InvalidMessage,
        ProtocolMismatch,
        Duplicate,
        DuplicateNativeEffect,
        InsufficientEntitlement,
        InsufficientPermission,
        CommandDisabled,
        GlobalDisabled,
        NotInMap,
        RankedMap,
        OnCooldown,
        UnknownCommand,
        ProcessorRejected,
        ExecutionFailed,
        Cancelled,
        ClientLimitReached
    }

    internal sealed class IntegrationInboundMessage
    {
        internal string Id { get; set; } = string.Empty;
        internal IntegrationMessageType MessageType { get; set; } = IntegrationMessageType.Unknown;
        internal JObject Payload { get; set; }
        internal string RawJson { get; set; } = string.Empty;
    }

    internal sealed class IntegrationViewerPayload
    {
        [JsonProperty("service")]
        internal string Service { get; set; } = "twitch";

        [JsonProperty("id")]
        internal string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        internal string Name { get; set; } = string.Empty;

        [JsonProperty("roles")]
        internal IntegrationViewerRolesPayload Roles { get; set; } = new IntegrationViewerRolesPayload();
    }

    internal sealed class IntegrationViewerRolesPayload
    {
        [JsonProperty("moderator")]
        internal bool Moderator { get; set; }

        [JsonProperty("vip")]
        internal bool Vip { get; set; }

        [JsonProperty("subscriber")]
        internal bool Subscriber { get; set; }

        [JsonProperty("broadcaster")]
        internal bool Broadcaster { get; set; }
    }

    internal static class IntegrationApiProtocol
    {
        internal static bool TryParseInbound(string rawJson, out IntegrationInboundMessage message, out string error)
        {
            message = null;
            error = null;

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                error = "Message body was empty.";
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(rawJson);
            }
            catch (JsonException ex)
            {
                error = "Invalid JSON: " + ex.Message;
                return false;
            }

            string protocol = root.Value<string>("protocol") ?? string.Empty;
            if (!string.Equals(protocol, IntegrationApiConstants.ProtocolName, StringComparison.Ordinal))
            {
                error = "Unsupported protocol.";
                return false;
            }

            int version = root.Value<int?>("version") ?? 0;
            if (version != IntegrationApiConstants.ProtocolVersion)
            {
                error = "Unsupported protocol version.";
                return false;
            }

            string type = root.Value<string>("type") ?? string.Empty;
            IntegrationMessageType messageType = ParseMessageType(type);
            if (messageType == IntegrationMessageType.Unknown)
            {
                error = "Unsupported message type.";
                return false;
            }

            message = new IntegrationInboundMessage
            {
                Id = root.Value<string>("id") ?? string.Empty,
                MessageType = messageType,
                Payload = root["payload"] as JObject ?? new JObject(),
                RawJson = rawJson
            };
            return true;
        }

        internal static string BuildEnvelope(string type, string id, JObject payload, string correlationId = null)
        {
            var envelope = new JObject
            {
                ["protocol"] = IntegrationApiConstants.ProtocolName,
                ["version"] = IntegrationApiConstants.ProtocolVersion,
                ["type"] = type,
                ["id"] = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["payload"] = payload ?? new JObject()
            };

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                envelope["correlationId"] = correlationId;
            }

            return envelope.ToString(Formatting.None);
        }

        internal static IntegrationRejectReason MapCommandRejectReason(CommandRejectReason reason, string processorMessage)
        {
            switch (reason)
            {
                case CommandRejectReason.InsufficientPermission:
                    return IntegrationRejectReason.InsufficientPermission;
                case CommandRejectReason.CommandDisabled:
                    return IntegrationRejectReason.CommandDisabled;
                case CommandRejectReason.GlobalDisabled:
                    return IntegrationRejectReason.GlobalDisabled;
                case CommandRejectReason.OnCooldown:
                    return IntegrationRejectReason.OnCooldown;
                case CommandRejectReason.UnknownCommand:
                    return IntegrationRejectReason.UnknownCommand;
                case CommandRejectReason.RankedMap:
                    return IntegrationRejectReason.RankedMap;
                case CommandRejectReason.InsufficientEntitlement:
                    return IntegrationRejectReason.InsufficientEntitlement;
                case CommandRejectReason.Cancelled:
                    return IntegrationRejectReason.Cancelled;
                case CommandRejectReason.ExecutionFailed:
                    return IntegrationRejectReason.ExecutionFailed;
                case CommandRejectReason.ProcessorRejected:
                    return LooksLikeEntitlementFailure(processorMessage)
                        ? IntegrationRejectReason.InsufficientEntitlement
                        : IntegrationRejectReason.ProcessorRejected;
                default:
                    return IntegrationRejectReason.ProcessorRejected;
            }
        }

        internal static bool ShouldRecommendRefund(IntegrationRejectReason reason)
        {
            switch (reason)
            {
                case IntegrationRejectReason.NotInMap:
                case IntegrationRejectReason.RankedMap:
                case IntegrationRejectReason.CommandDisabled:
                case IntegrationRejectReason.GlobalDisabled:
                case IntegrationRejectReason.OnCooldown:
                case IntegrationRejectReason.InsufficientEntitlement:
                case IntegrationRejectReason.InsufficientPermission:
                case IntegrationRejectReason.UnknownCommand:
                case IntegrationRejectReason.ProcessorRejected:
                case IntegrationRejectReason.ExecutionFailed:
                    return true;
                default:
                    return false;
            }
        }

        internal static ChatContext BuildChatContext(IntegrationViewerPayload viewer, string messageText, TriggerSource triggerSource)
        {
            IntegrationViewerPayload safeViewer = viewer ?? new IntegrationViewerPayload();
            IntegrationViewerRolesPayload roles = safeViewer.Roles ?? new IntegrationViewerRolesPayload();
            string senderName = string.IsNullOrWhiteSpace(safeViewer.Name) ? "Unknown" : safeViewer.Name.Trim();

            return new ChatContext
            {
                SenderName = senderName,
                MessageText = messageText ?? string.Empty,
                Source = ChatSource.ExternalApi,
                TriggerSource = triggerSource,
                IsModerator = roles.Moderator,
                IsVip = roles.Vip,
                IsSubscriber = roles.Subscriber,
                IsBroadcaster = roles.Broadcaster
            };
        }

        private static IntegrationMessageType ParseMessageType(string type)
        {
            switch ((type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "handshake":
                    return IntegrationMessageType.Handshake;
                case "command.invoke":
                    return IntegrationMessageType.CommandInvoke;
                case "event.raise":
                    return IntegrationMessageType.EventRaise;
                case "ping":
                    return IntegrationMessageType.Ping;
                default:
                    return IntegrationMessageType.Unknown;
            }
        }

        private static bool LooksLikeEntitlementFailure(string processorMessage)
        {
            if (string.IsNullOrWhiteSpace(processorMessage))
            {
                return false;
            }

            return processorMessage.IndexOf("entitlement", StringComparison.OrdinalIgnoreCase) >= 0
                || processorMessage.IndexOf("Tier 1", StringComparison.OrdinalIgnoreCase) >= 0
                || processorMessage.IndexOf("logged-in Twitch or Patreon", StringComparison.OrdinalIgnoreCase) >= 0
                || processorMessage.IndexOf("Supporter tab", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
