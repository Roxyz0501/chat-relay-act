using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ChatRelay
{
    internal sealed class BotWebhooks
    {
        private const string Api = "https://discord.com/api/v10/";
        private const string HookName = "FFXIV Chat Relay";
        private readonly HttpClient client;
        private readonly string token;
        private readonly Dictionary<string, Uri> endpoints = new Dictionary<string, Uri>();
        private string ownerId;
        private DateTime authRetry;
        private readonly Dictionary<string, DateTime> channelRetry = new Dictionary<string, DateTime>();
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        internal BotWebhooks(HttpClient client, string token) { this.client = client; this.token = token; }
        internal static bool IsChannelId(string id) => id != null && Regex.IsMatch(id, @"\A[1-9][0-9]{14,19}\z") && ulong.TryParse(id, out _);
        internal void Forget(string channel) => endpoints.Remove(channel);
        internal async Task<Uri> GetAsync(string channel, CancellationToken cancel)
        {
            if (!IsChannelId(channel)) throw new InvalidOperationException("InvalidChannel");
            Uri endpoint;
            if (endpoints.TryGetValue(channel, out endpoint)) return endpoint;
            var now = DateTime.UtcNow;
            if (authRetry > now || (channelRetry.TryGetValue(channel, out DateTime until) && until > now))
                throw new HttpRequestException("BotSetup");
            try { return await DiscoverAsync(channel, cancel).ConfigureAwait(false); }
            catch (HttpRequestException)
            {
                channelRetry[channel] = now.AddMinutes(2);
                throw;
            }
        }
        private async Task<Uri> DiscoverAsync(string channel, CancellationToken cancel)
        {
            Uri endpoint;
            if (ownerId == null)
            {
                var user = Json.Deserialize<Dictionary<string, object>>(await RequestAsync("users/@me", null, cancel).ConfigureAwait(false));
                ownerId = Value(user, "id");
                if (!IsChannelId(ownerId)) { ownerId = null; throw new InvalidOperationException("BotSetup"); }
            }
            var path = "channels/" + channel + "/webhooks";
            var hooks = Json.Deserialize<Dictionary<string, object>[]>(await RequestAsync(path, null, cancel).ConfigureAwait(false));
            foreach (var hook in hooks)
            {
                object owner;
                if (Value(hook, "name") == HookName && hook.TryGetValue("user", out owner) &&
                    Value(owner as Dictionary<string, object>, "id") == ownerId && TryHook(hook, channel, out endpoint))
                    return endpoints[channel] = endpoint;
            }
            var created = Json.Deserialize<Dictionary<string, object>>(await RequestAsync(path, Json.Serialize(new { name = HookName }), cancel).ConfigureAwait(false));
            if (!TryHook(created, channel, out endpoint)) throw new InvalidOperationException("BotSetup");
            return endpoints[channel] = endpoint;
        }
        private static string Value(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? Convert.ToString(value) : null;
        }
        private static bool TryHook(Dictionary<string, object> hook, string channel, out Uri endpoint)
        {
            endpoint = null;
            Uri url;
            if (Value(hook, "type") != "1" || Value(hook, "channel_id") != channel ||
                !RelaySender.TryEndpoint(Api + "webhooks/" + Value(hook,"id") + "/" + Value(hook,"token"), out url)) return false;
            endpoint = new Uri(url.AbsoluteUri + "?wait=true");
            return true;
        }
        private async Task<string> RequestAsync(string path, string json, CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(json == null ? HttpMethod.Get : HttpMethod.Post, Api + path))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bot", token);
                if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request, cancel).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 429)
                    {
                        var delay = Math.Max(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0,
                            RelaySender.RetryDelay(await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
                        throw new Cooldown(delay);
                    }
                    if ((int)response.StatusCode == 401) authRetry = DateTime.UtcNow.AddMinutes(2);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("BotSetup");
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }
        internal sealed class Cooldown : Exception
        {
            internal double Seconds { get; }
            internal Cooldown(double seconds) { Seconds = seconds; }
        }
    }
}
