using System;
using System.Text;
using Newtonsoft.Json.Linq;
using Chaos.NaCl;

namespace BeatSurgeon.Twitch
{
    internal static class JwtEd25519
    {
        // IMPORTANT:
        // Put your Ed25519 PUBLIC KEY here as 32 raw bytes (base64 or hex -> bytes).
        // This must match the server's ed25519-public.pem.
        private static readonly byte[] PublicKey = Convert.FromBase64String("UoawyekLLTw2ilbTqqNCeBwomcldgUN+oq/VmDydyD4=");

        internal sealed class VerifiedJwt
        {
            public JObject Header;
            public JObject Payload;
            public string SigningInput;   // "base64url(header).base64url(payload)"
            public byte[] SignatureBytes; // decoded signature
        }

        public static bool TryVerify(string jwt, out VerifiedJwt verified)
        {
            verified = null;
            if (string.IsNullOrWhiteSpace(jwt)) return false;

            var parts = jwt.Split('.');
            if (parts.Length != 3) return false;

            string headerB64 = parts[0];
            string payloadB64 = parts[1];
            string sigB64 = parts[2];

            byte[] headerJson = Base64UrlDecode(headerB64);
            byte[] payloadJson = Base64UrlDecode(payloadB64);
            byte[] sig = Base64UrlDecode(sigB64);

            var header = JObject.Parse(Encoding.UTF8.GetString(headerJson));
            var payload = JObject.Parse(Encoding.UTF8.GetString(payloadJson));

            string alg = header["alg"]?.ToString();
            if (!string.Equals(alg, "EdDSA", StringComparison.Ordinal)) return false;

            string signingInput = headerB64 + "." + payloadB64;
            byte[] signingBytes = Encoding.ASCII.GetBytes(signingInput);

            bool ok = Ed25519.Verify(sig, signingBytes, PublicKey);
            if (!ok) return false;

            verified = new VerifiedJwt
            {
                Header = header,
                Payload = payload,
                SigningInput = signingInput,
                SignatureBytes = sig
            };
            return true;
        }

        private static byte[] Base64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 0: break;
                case 2: s += "=="; break;
                case 3: s += "="; break;
                default: throw new FormatException("Invalid base64url length");
            }
            return Convert.FromBase64String(s);
        }
    }
}
