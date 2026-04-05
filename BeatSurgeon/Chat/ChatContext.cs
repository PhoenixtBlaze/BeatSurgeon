using System;

namespace BeatSurgeon.Chat
{
    internal enum ChatSource
    {
        Unknown,
        NativeTwitch,
        ChatPlex
    }

    internal enum TriggerSource
    {
        Chat,
        ChannelPoints
    }

    /// <summary>
    /// Pure chat message context model used by command processors.
    /// </summary>
    internal sealed class ChatContext
    {
        internal string SenderName { get; set; } = "Unknown";
        internal string MessageText { get; set; } = string.Empty;

        internal bool IsModerator { get; set; }
        internal bool IsVip { get; set; }
        internal bool IsSubscriber { get; set; }
        internal bool IsBroadcaster { get; set; }
        internal int Bits { get; set; }

        internal object RawService { get; set; }
        internal object RawMessage { get; set; }
        internal ChatSource Source { get; set; } = ChatSource.Unknown;
        internal TriggerSource TriggerSource { get; set; } = TriggerSource.Chat;
        internal bool IsChannelPoint { get; set; }

        internal Func<string, bool> CooldownChecker { private get; set; }

        internal string Username => SenderName;

        internal string Command
        {
            get
            {
                if (!TryExtractFirstCommandToken(MessageText, out string command))
                {
                    return string.Empty;
                }

                return command;
            }
        }

        internal static bool TryExtractFirstCommandToken(string messageText, out string commandToken)
        {
            int start;
            int length;
            bool found = TryExtractFirstCommandToken(messageText, out commandToken, out start, out length);
            return found;
        }

        internal static bool TryExtractFirstCommandToken(string messageText, out string commandToken, out int start, out int length)
        {
            commandToken = string.Empty;
            start = -1;
            length = 0;

            if (string.IsNullOrWhiteSpace(messageText))
            {
                return false;
            }

            if (messageText.Length < 2 || messageText[0] != '!')
            {
                return false;
            }

            char first = messageText[1];
            if (!char.IsLetter(first))
            {
                return false;
            }

            int j = 2;
            while (j < messageText.Length)
            {
                char c = messageText[j];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    j++;
                    continue;
                }

                break;
            }

            int tokenLength = j;
            if (tokenLength <= 1)
            {
                return false;
            }

            start = 0;
            length = tokenLength;
            commandToken = messageText.Substring(0, tokenLength);
            return true;
        }

        internal bool HasPermission(string required)
        {
            if (string.IsNullOrWhiteSpace(required) ||
                string.Equals(required, "everyone", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(required, "subscriber", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(required, "sub", StringComparison.OrdinalIgnoreCase))
            {
                return IsSubscriber || IsModerator || IsBroadcaster;
            }

            if (string.Equals(required, "vip", StringComparison.OrdinalIgnoreCase))
            {
                return IsVip || IsModerator || IsBroadcaster;
            }

            if (string.Equals(required, "moderator", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(required, "mod", StringComparison.OrdinalIgnoreCase))
            {
                return IsModerator || IsBroadcaster;
            }

            if (string.Equals(required, "broadcaster", StringComparison.OrdinalIgnoreCase))
            {
                return IsBroadcaster;
            }

            return true;
        }

        internal bool IsOnCooldown(string commandKey)
        {
            return CooldownChecker != null && CooldownChecker(commandKey);
        }

        public override string ToString()
            => $"ChatContext[User={Username} IsMod={IsModerator} IsSub={IsSubscriber} Cmd={Command}]";
    }
}
