using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class ViewerSupporterLookupService : IInitializable, IDisposable
    {
        private const string BackendBaseUrl = "https://phoenixblaze0.duckdns.org";
        private const int MaxCacheEntries = 512;
        private static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan LoginIdTtl = TimeSpan.FromHours(6);

        private static readonly LogUtil _log = LogUtil.GetLogger("ViewerSupporter");
        private static ViewerSupporterLookupService _instance;

        private readonly object _gate = new object();
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly Queue<string> _order = new Queue<string>();
        private readonly Dictionary<string, LoginCacheEntry> _loginToId = new Dictionary<string, LoginCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly HttpClient _http;
        private int _reportInFlight;

        internal static ViewerSupporterLookupService Instance => _instance;

        [Inject]
        public ViewerSupporterLookupService()
        {
            _instance = this;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void Initialize()
        {
            TwitchAuthManager.Instance.OnAuthReady += OnIdentityReady;
            PatreonAuthManager.Instance.OnAuthReady += OnIdentityReady;
            TryReportCurrentIdentities();
        }

        public void Dispose()
        {
            TwitchAuthManager.Instance.OnAuthReady -= OnIdentityReady;
            PatreonAuthManager.Instance.OnAuthReady -= OnIdentityReady;
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }

            _http.Dispose();
        }

        internal async Task<bool> IsViewerSupporterAsync(ChatContext ctx, CancellationToken ct)
        {
            string twitchUserId = ctx != null ? ctx.SenderTwitchUserId : null;
            if (string.IsNullOrWhiteSpace(twitchUserId) && ctx != null && !string.IsNullOrWhiteSpace(ctx.SenderName))
            {
                twitchUserId = await ResolveUserIdByLoginAsync(ctx.SenderName, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(twitchUserId) && ctx != null)
                {
                    ctx.SenderTwitchUserId = twitchUserId;
                }
            }

            if (string.IsNullOrWhiteSpace(twitchUserId) || !IsTwitchUserId(twitchUserId))
            {
                _log.Warn("ViewerSupporter lookup denied: missing twitch user id user=" + (ctx != null ? ctx.SenderName : "?"));
                return false;
            }

            CacheEntry cached;
            lock (_gate)
            {
                if (_cache.TryGetValue(twitchUserId, out cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
                {
                    _log.Info("ViewerSupporter lookup id=" + twitchUserId + " allowed=" + cached.Allowed + " source=" + cached.Source + " cached=true");
                    return cached.Allowed;
                }
            }

            ViewerLookupResult result = await FetchViewerAsync(twitchUserId, ct).ConfigureAwait(false);
            Store(twitchUserId, result);
            _log.Info(
                "ViewerSupporter lookup id=" + twitchUserId
                + " allowed=" + result.Allowed
                + " source=" + result.Source
                + " cached=false");
            return result.Allowed;
        }

        internal static void TryReportCurrentIdentities()
        {
            ViewerSupporterLookupService instance = _instance;
            if (instance == null)
            {
                return;
            }

            instance.OnIdentityReady();
        }

        private void OnIdentityReady()
        {
            _ = ReportCurrentIdentitiesAsync(CancellationToken.None);
        }

        private async Task ReportCurrentIdentitiesAsync(CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref _reportInFlight, 1, 0) != 0)
            {
                return;
            }

            try
            {
                string twitchToken = null;
                string patreonToken = null;
                try
                {
                    if (TwitchAuthManager.Instance.IsAuthenticated && !TwitchAuthManager.Instance.IsReauthRequired)
                    {
                        twitchToken = await TwitchAuthManager.Instance.GetAccessTokenAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("SupporterLink twitch token read failed: " + ex.Message);
                }

                try
                {
                    if (PatreonAuthManager.Instance.IsAuthenticated && !PatreonAuthManager.Instance.IsReauthRequired)
                    {
                        patreonToken = await PatreonAuthManager.Instance.GetAccessTokenAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("SupporterLink patreon token read failed: " + ex.Message);
                }

                if (string.IsNullOrWhiteSpace(twitchToken) && string.IsNullOrWhiteSpace(patreonToken))
                {
                    return;
                }

                using (var request = new HttpRequestMessage(HttpMethod.Post, BackendBaseUrl + "/supporters/link"))
                {
                    if (!string.IsNullOrWhiteSpace(twitchToken))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", twitchToken);
                        if (!string.IsNullOrWhiteSpace(patreonToken))
                        {
                            request.Headers.TryAddWithoutValidation("X-Patreon-Authorization", "Bearer " + patreonToken);
                        }
                    }
                    else
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patreonToken);
                    }

                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            _log.Warn("SupporterLink failed status=" + (int)response.StatusCode);
                            return;
                        }

                        string twitchId = string.Empty;
                        string patreonId = string.Empty;
                        try
                        {
                            JObject json = JObject.Parse(body);
                            twitchId = json["twitchUserId"]?.ToString() ?? string.Empty;
                            patreonId = json["patreonUserId"]?.ToString() ?? string.Empty;
                        }
                        catch
                        {
                            // Response shape is informational only.
                        }

                        _log.Info("SupporterLink reported twitchId=" + twitchId + " patreonId=" + patreonId);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("SupporterLink exception: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _reportInFlight, 0);
            }
        }

        private async Task<ViewerLookupResult> FetchViewerAsync(string twitchUserId, CancellationToken ct)
        {
            try
            {
                string token = await TwitchAuthManager.Instance.GetAccessTokenAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return ViewerLookupResult.Denied;
                }

                using (var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    BackendBaseUrl + "/entitlements/viewer/" + Uri.EscapeDataString(twitchUserId)))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using (HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _log.Warn("ViewerSupporter lookup http status=" + (int)response.StatusCode + " id=" + twitchUserId);
                            return ViewerLookupResult.Denied;
                        }

                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        JObject json = JObject.Parse(body);
                        bool allowed = json["isSupporter"]?.Value<bool>() == true;
                        int tier = json["tier"]?.Value<int>() ?? 0;
                        string source = json["source"]?.ToString() ?? "none";
                        bool patreonSource = string.Equals(source, "patreon", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(source, "both", StringComparison.OrdinalIgnoreCase);
                        if (!allowed || tier < 1 || !patreonSource)
                        {
                            return new ViewerLookupResult { Allowed = false, Source = source ?? "none" };
                        }

                        return new ViewerLookupResult { Allowed = true, Source = source };
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("ViewerSupporter lookup failed id=" + twitchUserId + " err=" + ex.Message);
                return ViewerLookupResult.Denied;
            }
        }

        private async Task<string> ResolveUserIdByLoginAsync(string login, CancellationToken ct)
        {
            string normalized = (login ?? string.Empty).Trim().TrimStart('@');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            lock (_gate)
            {
                LoginCacheEntry loginEntry;
                if (_loginToId.TryGetValue(normalized, out loginEntry) && loginEntry.ExpiresAtUtc > DateTime.UtcNow)
                {
                    return loginEntry.UserId ?? string.Empty;
                }
            }

            string userId = await TwitchApiClient.Instance.ResolveUserIdByLoginAsync(normalized, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return string.Empty;
            }

            lock (_gate)
            {
                _loginToId[normalized] = new LoginCacheEntry
                {
                    UserId = userId,
                    ExpiresAtUtc = DateTime.UtcNow.Add(LoginIdTtl)
                };
            }

            return userId;
        }

        private void Store(string twitchUserId, ViewerLookupResult result)
        {
            DateTime expires = DateTime.UtcNow.Add(result.Allowed ? PositiveTtl : NegativeTtl);
            lock (_gate)
            {
                if (!_cache.ContainsKey(twitchUserId))
                {
                    _order.Enqueue(twitchUserId);
                }

                _cache[twitchUserId] = new CacheEntry
                {
                    Allowed = result.Allowed,
                    Source = result.Source,
                    ExpiresAtUtc = expires
                };

                while (_cache.Count > MaxCacheEntries && _order.Count > 0)
                {
                    string oldest = _order.Dequeue();
                    _cache.Remove(oldest);
                }
            }
        }

        private static bool IsTwitchUserId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 20)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private struct CacheEntry
        {
            internal bool Allowed;
            internal string Source;
            internal DateTime ExpiresAtUtc;
        }

        private struct LoginCacheEntry
        {
            internal string UserId;
            internal DateTime ExpiresAtUtc;
        }

        private struct ViewerLookupResult
        {
            internal static readonly ViewerLookupResult Denied = new ViewerLookupResult { Allowed = false, Source = "none" };
            internal bool Allowed;
            internal string Source;
        }
    }
}
