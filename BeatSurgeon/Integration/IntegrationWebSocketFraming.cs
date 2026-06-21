using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeatSurgeon.Integration
{
    internal static class IntegrationWebSocketFraming
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        internal static async Task<bool> TryAcceptHandshakeAsync(
            Stream stream,
            string expectedPath,
            string expectedToken,
            CancellationToken ct)
        {
            if (stream == null)
            {
                return false;
            }

            string requestText = await ReadHttpHeadersAsync(stream, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestText))
            {
                return false;
            }

            if (!TryParseHandshakeRequest(requestText, out string requestPath, out string webSocketKey, out string queryToken))
            {
                await WriteHttpResponseAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false);
                return false;
            }

            if (!string.Equals(requestPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                await WriteHttpResponseAsync(stream, 404, "Not Found", ct).ConfigureAwait(false);
                return false;
            }

            if (!IsAuthorized(expectedToken, queryToken))
            {
                await WriteHttpResponseAsync(stream, 401, "Unauthorized", ct).ConfigureAwait(false);
                return false;
            }

            if (string.IsNullOrWhiteSpace(webSocketKey))
            {
                await WriteHttpResponseAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false);
                return false;
            }

            string accept = ComputeWebSocketAccept(webSocketKey);
            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";

            byte[] responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            return true;
        }

        internal static async Task<string> ReceiveTextMessageAsync(Stream stream, CancellationToken ct)
        {
            byte[] header = await ReadExactAsync(stream, 2, ct).ConfigureAwait(false);
            if (header == null)
            {
                return null;
            }

            int opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            ulong payloadLength = (ulong)(header[1] & 0x7F);

            if (payloadLength == 126)
            {
                byte[] extended = await ReadExactAsync(stream, 2, ct).ConfigureAwait(false);
                if (extended == null)
                {
                    return null;
                }

                payloadLength = (ushort)((extended[0] << 8) | extended[1]);
            }
            else if (payloadLength == 127)
            {
                byte[] extended = await ReadExactAsync(stream, 8, ct).ConfigureAwait(false);
                if (extended == null)
                {
                    return null;
                }

                payloadLength = 0;
                for (int i = 0; i < 8; i++)
                {
                    payloadLength = (payloadLength << 8) | extended[i];
                }
            }

            if (payloadLength > IntegrationApiConstants.MaxMessageBytes)
            {
                return null;
            }

            byte[] mask = null;
            if (masked)
            {
                mask = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
                if (mask == null)
                {
                    return null;
                }
            }

            byte[] payload = payloadLength == 0
                ? Array.Empty<byte>()
                : await ReadExactAsync(stream, (int)payloadLength, ct).ConfigureAwait(false);

            if (payload == null)
            {
                return null;
            }

            if (masked)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(payload[i] ^ mask[i % 4]);
                }
            }

            if (opcode == 0x8)
            {
                return null;
            }

            if (opcode != 0x1)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(payload);
        }

        internal static async Task SendTextMessageAsync(Stream stream, string message, CancellationToken ct)
        {
            if (stream == null)
            {
                return;
            }

            byte[] payload = Encoding.UTF8.GetBytes(message ?? string.Empty);
            if (payload.Length > IntegrationApiConstants.MaxMessageBytes)
            {
                throw new InvalidOperationException("Integration API outbound message exceeded size limit.");
            }

            using (var frame = new MemoryStream())
            {
                frame.WriteByte(0x81);
                if (payload.Length <= 125)
                {
                    frame.WriteByte((byte)payload.Length);
                }
                else if (payload.Length <= ushort.MaxValue)
                {
                    frame.WriteByte(126);
                    frame.WriteByte((byte)((payload.Length >> 8) & 0xFF));
                    frame.WriteByte((byte)(payload.Length & 0xFF));
                }
                else
                {
                    frame.WriteByte(127);
                    for (int shift = 56; shift >= 0; shift -= 8)
                    {
                        frame.WriteByte((byte)((payload.Length >> shift) & 0xFF));
                    }
                }

                frame.Write(payload, 0, payload.Length);
                byte[] bytes = frame.ToArray();
                await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
        }

        internal static async Task SendCloseAsync(Stream stream, CancellationToken ct)
        {
            byte[] frame = { 0x88, 0x00 };
            await stream.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static async Task<string> ReadHttpHeadersAsync(Stream stream, CancellationToken ct)
        {
            var buffer = new byte[IntegrationApiConstants.MaxMessageBytes];
            int total = 0;

            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, total, buffer.Length - total, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    return null;
                }

                total += read;
                string current = Encoding.ASCII.GetString(buffer, 0, total);
                int headerEnd = current.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd >= 0)
                {
                    return current.Substring(0, headerEnd);
                }
            }

            return null;
        }

        private static bool TryParseHandshakeRequest(
            string requestText,
            out string requestPath,
            out string webSocketKey,
            out string queryToken)
        {
            requestPath = string.Empty;
            webSocketKey = string.Empty;
            queryToken = string.Empty;

            string[] lines = requestText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                return false;
            }

            string[] requestLine = lines[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (requestLine.Length < 2 || !string.Equals(requestLine[0], "GET", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rawTarget = requestLine[1];
            int queryIndex = rawTarget.IndexOf('?');
            if (queryIndex >= 0)
            {
                requestPath = rawTarget.Substring(0, queryIndex);
                string query = rawTarget.Substring(queryIndex + 1);
                queryToken = ParseQueryToken(query);
            }
            else
            {
                requestPath = rawTarget;
            }

            bool hasUpgrade = false;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                int separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                string headerName = line.Substring(0, separator).Trim();
                string headerValue = line.Substring(separator + 1).Trim();

                if (string.Equals(headerName, "Upgrade", StringComparison.OrdinalIgnoreCase)
                    && headerValue.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasUpgrade = true;
                }

                if (string.Equals(headerName, "Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                {
                    webSocketKey = headerValue;
                }
            }

            return hasUpgrade;
        }

        private static string ParseQueryToken(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return string.Empty;
            }

            string[] parts = query.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                string[] pair = parts[i].Split(new[] { '=' }, 2);
                if (pair.Length == 2 && string.Equals(pair[0], "token", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }

            return string.Empty;
        }

        private static bool IsAuthorized(string expectedToken, string queryToken)
        {
            if (string.IsNullOrWhiteSpace(expectedToken))
            {
                return false;
            }

            return string.Equals(expectedToken, queryToken ?? string.Empty, StringComparison.Ordinal);
        }

        private static string ComputeWebSocketAccept(string webSocketKey)
        {
            string combined = webSocketKey + WebSocketGuid;
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(combined));
                return Convert.ToBase64String(hash);
            }
        }

        private static async Task WriteHttpResponseAsync(Stream stream, int statusCode, string statusText, CancellationToken ct)
        {
            string response = "HTTP/1.1 " + statusCode + " " + statusText + "\r\nConnection: close\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken ct)
        {
            if (length == 0)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    return null;
                }

                offset += read;
            }

            return buffer;
        }
    }
}
