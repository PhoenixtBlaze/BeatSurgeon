using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;

namespace BeatSurgeon.Twitch
{
    internal static class JwtEd25519
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("JwtEd25519");
        private static readonly Lazy<bool> _ed25519Resolved = new Lazy<bool>(ResolveEd25519);
        private static MethodInfo _verifyMethod;
        private static MethodInfo _signMethod;

        // Public verification key (must match backend signer).
        private static readonly byte[] PublicKey = Convert.FromBase64String("UoawyekLLTw2ilbTqqNCeBwomcldgUN+oq/VmDydyD4=");

        internal sealed class VerifiedJwt
        {
            internal JObject Header;
            internal JObject Payload;
            internal string SigningInput;
            internal byte[] SignatureBytes;
        }

        internal static bool TryVerify(string jwt, out VerifiedJwt verified)
        {
            verified = null;
            if (string.IsNullOrWhiteSpace(jwt)) return false;

            string[] parts = jwt.Split('.');
            if (parts.Length != 3) return false;

            try
            {
                string headerB64 = parts[0];
                string payloadB64 = parts[1];
                string sigB64 = parts[2];

                byte[] headerJson = Base64UrlDecode(headerB64);
                byte[] payloadJson = Base64UrlDecode(payloadB64);
                byte[] sig = Base64UrlDecode(sigB64);

                JObject header = JObject.Parse(Encoding.UTF8.GetString(headerJson));
                JObject payload = JObject.Parse(Encoding.UTF8.GetString(payloadJson));

                string alg = header["alg"]?.ToString();
                if (!string.Equals(alg, "EdDSA", StringComparison.Ordinal)) return false;

                string signingInput = headerB64 + "." + payloadB64;
                byte[] signingBytes = Encoding.ASCII.GetBytes(signingInput);
                bool ok = Verify(sig, signingBytes, PublicKey);
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
            catch (Exception ex)
            {
                _log.Exception(ex, "TryVerify");
                return false;
            }
        }

        internal static string Sign(string payload, byte[] privateKey)
        {
            if (privateKey == null || privateKey.Length == 0)
            {
                _log.Critical("Sign called with null/empty private key - JWT will fail");
                throw new ArgumentException("Private key cannot be null or empty", nameof(privateKey));
            }

            _log.Debug("Signing JWT payload");
            string header = "{\"alg\":\"EdDSA\",\"typ\":\"JWT\"}";
            string headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
            string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload ?? "{}"));
            string signingInput = headerB64 + "." + payloadB64;
            byte[] signature = SignBytes(Encoding.ASCII.GetBytes(signingInput), privateKey);
            string token = signingInput + "." + Base64UrlEncode(signature);
            _log.Debug("JWT signed OK");
            return token;
        }

        private static bool ResolveEd25519()
        {
            try
            {
                Assembly chaosAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a =>
                    {
                        try { return string.Equals(a.GetName().Name, "Chaos.NaCl", StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    });

                if (chaosAsm == null)
                {
                    Assembly self = Assembly.GetExecutingAssembly();
                    string resourceName = self.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("Chaos.NaCl.dll", StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrWhiteSpace(resourceName))
                    {
                        using (Stream stream = self.GetManifestResourceStream(resourceName))
                        using (var ms = new MemoryStream())
                        {
                            if (stream != null)
                            {
                                stream.CopyTo(ms);
                                chaosAsm = Assembly.Load(ms.ToArray());
                            }
                        }
                    }
                }

                if (chaosAsm == null)
                {
                    _log.Error("Chaos.NaCl assembly could not be resolved for JWT verification.");
                    return false;
                }

                Type ed25519Type = chaosAsm.GetType("Chaos.NaCl.Ed25519", throwOnError: false);
                if (ed25519Type == null)
                {
                    _log.Error("Chaos.NaCl.Ed25519 type not found in resolved assembly.");
                    return false;
                }

                _verifyMethod = ed25519Type.GetMethod(
                    "Verify",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(byte[]), typeof(byte[]), typeof(byte[]) },
                    modifiers: null);

                _signMethod = ed25519Type.GetMethod(
                    "Sign",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(byte[]), typeof(byte[]) },
                    modifiers: null);

                bool ok = _verifyMethod != null && _signMethod != null;
                if (!ok)
                {
                    _log.Error("Chaos.NaCl.Ed25519 methods could not be bound.");
                }

                return ok;
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "ResolveEd25519");
                return false;
            }
        }

        private static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
        {
            if (!_ed25519Resolved.Value) return false;

            try
            {
                object result = _verifyMethod.Invoke(null, new object[] { signature, message, publicKey });
                return result is bool b && b;
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Verify");
                return false;
            }
        }

        private static byte[] SignBytes(byte[] message, byte[] privateKey)
        {
            if (!_ed25519Resolved.Value)
            {
                throw new InvalidOperationException("Chaos.NaCl Ed25519 is unavailable.");
            }

            try
            {
                object result = _signMethod.Invoke(null, new object[] { message, privateKey });
                if (result is byte[] signature)
                {
                    return signature;
                }

                throw new InvalidOperationException("Chaos.NaCl.Ed25519.Sign returned unexpected result.");
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 0:
                    break;
                case 2:
                    s += "==";
                    break;
                case 3:
                    s += "=";
                    break;
                default:
                    throw new FormatException("Invalid base64url length");
            }
            return Convert.FromBase64String(s);
        }
    }
}
