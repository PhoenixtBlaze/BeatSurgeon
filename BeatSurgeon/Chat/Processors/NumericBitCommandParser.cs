using System;

namespace BeatSurgeon.Chat.Processors
{
    internal static class NumericBitCommandParser
    {
        internal static int ParseRequestedBits(string messageText, string commandName)
        {
            string usage = "Usage: " + commandName + " <bits>  e.g. " + commandName + " 10 or " + commandName + " 100";
            if (string.IsNullOrWhiteSpace(messageText))
            {
                throw new InvalidOperationException(usage);
            }

            if (!ChatContext.TryExtractFirstCommandToken(messageText, out _, out int commandStart, out int commandLength))
            {
                throw new InvalidOperationException(usage);
            }

            int suffixStart = commandStart + commandLength;
            if (suffixStart >= messageText.Length)
            {
                throw new InvalidOperationException(usage);
            }

            string raw = messageText.Substring(suffixStart).Trim();
            if (!int.TryParse(raw, out int parsed) || parsed <= 0)
            {
                throw new InvalidOperationException("Usage: " + commandName + " <bits>  where <bits> is a positive whole number.");
            }

            return parsed;
        }
    }
}