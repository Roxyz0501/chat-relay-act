using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ChatRelay
{
    // Consumed only by the single relay worker, including all cache updates.
    internal sealed class PortraitLookup : IDisposable
    {
        private readonly HttpClient client;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private DateTime cooldown;
        private sealed class Entry { internal string Url; internal DateTime Expires; }
        internal PortraitLookup(HttpMessageHandler handler = null)
        {
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) {
                Timeout = TimeSpan.FromSeconds(4), MaxResponseContentBufferSize = 512 * 1024 };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ChatRelay/1.2");
        }
        internal async Task<string> FindAsync(PlayerIdentity player, CancellationToken token)
        {
            if (player == null || string.IsNullOrEmpty(player.World)) return null;
            Entry entry;
            var now = DateTime.UtcNow;
            if (entries.TryGetValue(player.Display, out entry) && entry.Expires > now) return entry.Url;
            if (cooldown > now) return null;
            string url = null;
            try
            {
                var query = "https://na.finalfantasyxiv.com/lodestone/character/?q=" + Uri.EscapeDataString(player.Name) +
                    "&worldname=" + Uri.EscapeDataString(player.World);
                using (var response = await client.GetAsync(query, token).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                        url = Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false), player);
                    else if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                        cooldown = now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(2));
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { cooldown = now.AddMinutes(1); }
            catch (HttpRequestException) { cooldown = now.AddMinutes(1); }
            if (entries.Count >= 512 && !entries.ContainsKey(player.Display))
                entries.Remove(entries.OrderBy(x => x.Value.Expires).First().Key);
            entries[player.Display] = new Entry { Url = url, Expires = now.AddMinutes(url == null ? 15 : 1440) };
            return url;
        }
        internal static string Parse(string html, PlayerIdentity player)
        {
            foreach (Match item in Regex.Matches(html, @"<a\b[^>]*href=""/lodestone/character/\d+/""[^>]*>(.*?)</a>", RegexOptions.Singleline))
            {
                var card = item.Groups[1].Value;
                var name = WebUtility.HtmlDecode(Regex.Match(card, @"class=""entry__name"">([^<]+)").Groups[1].Value).Trim();
                var world = Regex.Match(card, @"class=""entry__world"">(?:<[^>]+>)*\s*([A-Za-z]+)").Groups[1].Value;
                if (!string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(player.World, world, StringComparison.OrdinalIgnoreCase)) continue;
                var image = Regex.Match(card, @"class=""entry__chara__face""[^>]*>\s*<img\b[^>]*\bsrc=""([^""]+)""");
                Uri uri;
                if (Uri.TryCreate(WebUtility.HtmlDecode(image.Groups[1].Value), UriKind.Absolute, out uri) &&
                    uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
                    uri.Host.EndsWith(".finalfantasyxiv.com", StringComparison.OrdinalIgnoreCase)) return uri.AbsoluteUri;
            }
            return null;
        }
        public void Dispose() => client.Dispose();
    }
}
