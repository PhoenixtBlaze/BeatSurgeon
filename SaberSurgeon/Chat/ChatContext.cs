namespace BeatSurgeon.Chat
{
    /// <summary>
    /// Metadata for a single chat message, extracted from ChatPlex IChatMessage.
    /// </summary>
    public enum ChatSource
    {
        Unknown,
        NativeTwitch,
        ChatPlex
    }

    public class ChatContext
    {
        public string SenderName { get; set; } = "Unknown";
        public string MessageText { get; set; } = "";

        // Roles
        public bool IsModerator { get; set; }
        public bool IsVip { get; set; }
        public bool IsSubscriber { get; set; }
        public bool IsBroadcaster { get; set; }

        // Bits / cheers
        public int Bits { get; set; }

        // Raw data
        public object RawService { get; set; }
        public object RawMessage { get; set; }

        public ChatSource Source { get; set; } = ChatSource.Unknown;

        public bool IsChannelPoint { get; set; }

    }
}
