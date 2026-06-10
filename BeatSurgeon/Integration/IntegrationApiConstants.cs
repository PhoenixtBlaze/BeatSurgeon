namespace BeatSurgeon.Integration
{
    internal static class IntegrationApiConstants
    {
        internal const string ProtocolName = "beatsurgeon.integration";
        internal const int ProtocolVersion = 1;
        internal const int DefaultPort = 47832;
        internal const int MaxClients = 3;
        internal const int MaxMessageBytes = 64 * 1024;
        internal const int CommandDedupWindowSeconds = 30;
        internal const int AutomaticEffectDedupWindowSeconds = 90;
        internal const string BindAddress = "127.0.0.1";
        internal const string PathPrefix = "/v1/integration";
    }
}
