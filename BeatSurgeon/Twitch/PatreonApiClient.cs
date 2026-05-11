using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class PatreonApiClient
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("PatreonApiClient");
        private static PatreonApiClient _instance;

        private readonly HttpClient _http;
        private readonly PatreonAuthManager _authManager;

        private const string BackendBaseUrl = "https://phoenixblaze0.duckdns.org";
        private const string BackendEntitlementsUrl = BackendBaseUrl + "/patreon/entitlements";
        private const string BackendVisualsPermissionUrl = BackendBaseUrl + "/patreon/visuals/permission";

        internal static PatreonApiClient Instance => _instance ?? (_instance = new PatreonApiClient(PatreonAuthManager.Instance));

        [Inject]
        public PatreonApiClient(PatreonAuthManager authManager)
        {
            _instance = this;
            _authManager = authManager;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _log.Lifecycle("HttpClient initialized for Patreon backend calls");
        }

        internal async Task RefreshEntitlementsAsync(string userAccessToken, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, BackendEntitlementsUrl))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
                    using (HttpResponseMessage response = await SendBackendAsync(request, ct).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            InvalidateSupporterState();
                            return;
                        }

                        string expectedUserId = await _authManager.GetUserIdAsync(ct).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(expectedUserId))
                        {
                            InvalidateSupporterState();
                            return;
                        }

                        JObject json = JObject.Parse(body);
                        string entitlementToken = json["entitlementToken"]?.ToString();
                        if (!EntitlementTokenValidator.TryVerifyAndParse(
                                entitlementToken,
                                expectedUserId,
                                EntitlementProvider.Patreon,
                                out EntitlementsSnapshot snapshot))
                        {
                            InvalidateSupporterState();
                            return;
                        }

                        EntitlementsState.Set(EntitlementProvider.Patreon, snapshot);
                        PluginConfig.Instance.CachedSupporterTier = (int)EntitlementsState.Current.Tier;
                        PremiumVisualFeatureAccessController.SyncAllConfigEnabledStates();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RefreshEntitlementsAsync");
                InvalidateSupporterState();
            }
        }

        internal async Task<bool> CheckVisualsPermissionAsync(CancellationToken ct = default(CancellationToken))
        {
            string accessToken = await _authManager.GetAccessTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            EntitlementsSnapshot current = EntitlementsState.Get(EntitlementProvider.Patreon);
            if (string.IsNullOrWhiteSpace(current.SignedEntitlementToken) ||
                DateTime.UtcNow >= current.ExpiresAtUtc.AddMinutes(-5))
            {
                _log.Info("Patreon entitlement token expired or missing, refreshing...");
                await RefreshEntitlementsAsync(accessToken, ct).ConfigureAwait(false);
                current = EntitlementsState.Get(EntitlementProvider.Patreon);
                if (string.IsNullOrWhiteSpace(current.SignedEntitlementToken))
                {
                    _log.Warn("Failed to refresh Patreon entitlement token");
                    return false;
                }
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, BackendVisualsPermissionUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.TryAddWithoutValidation("X-Entitlement", current.SignedEntitlementToken);

                using (HttpResponseMessage response = await SendBackendAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _log.Warn("Patreon CheckVisualsPermission failed: " + response.StatusCode + " - " + body);
                        return false;
                    }

                    try
                    {
                        return JObject.Parse(body)["allowed"]?.Value<bool>() == true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        private async Task<HttpResponseMessage> SendBackendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            _log.Debug($"HTTP {request.Method} {request.RequestUri} => {(int)response.StatusCode}");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _log.Warn("HTTP 401 Unauthorized on " + request.RequestUri + " - refreshing Patreon token for subsequent calls");
                await _authManager.ForceRefreshTokenAsync(ct).ConfigureAwait(false);
            }

            return response;
        }

        private static void InvalidateSupporterState()
        {
            EntitlementsState.Clear(EntitlementProvider.Patreon);
            if (PluginConfig.Instance != null)
            {
                PluginConfig.Instance.CachedSupporterTier = (int)EntitlementsState.Current.Tier;
            }

            PremiumVisualFeatureAccessController.SyncAllConfigEnabledStates();
        }
    }
}