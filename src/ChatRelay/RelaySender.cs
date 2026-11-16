using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ChatRelay
{
    // A sender belongs to one immutable configuration. Reconfiguration cancels it and discards its queue.
    internal sealed class RelaySender : IDisposable
    {
        private sealed class Post { internal string Text, Channel; internal PlayerIdentity Player; }
        private readonly ConcurrentQueue<Post> queue = new ConcurrentQueue<Post>();
        private readonly BotWebhooks hooks;
        private readonly PortraitLookup portraits;
        private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly HttpClient client;
        private readonly Uri endpoint;
        private readonly Task worker;
        private readonly object enqueueGate = new object();
        private int queued, disposed;
        internal string StatusKey = "Ready";
        internal int Sent, Dropped;
        internal RelaySender(Uri url, HttpMessageHandler handler = null, string botToken = null, HttpMessageHandler portraitHandler = null)
        {
            if (url != null) endpoint = new Uri(url.GetLeftPart(UriPartial.Path) + "?wait=true");
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
            if (!string.IsNullOrEmpty(botToken)) hooks = new BotWebhooks(client, botToken);
            portraits = new PortraitLookup(portraitHandler);
            worker = Task.Run(RunAsync);
        }
        internal static bool TryEndpoint(string value, out Uri uri)
        {
            uri = null; Uri candidate;
            if (!Uri.TryCreate(value, UriKind.Absolute, out candidate) || candidate.Scheme != "https" || !candidate.IsDefaultPort || candidate.UserInfo.Length != 0 || candidate.Query.Length != 0 || candidate.Fragment.Length != 0) return false;
            if (candidate.Host != "discord.com" && candidate.Host != "discordapp.com") return false;
            if (!Regex.IsMatch(candidate.AbsolutePath, @"^/api(?:/v10)?/webhooks/[0-9]{15,22}/[A-Za-z0-9_-]{20,200}$")) return false;
            uri = candidate; return true;
        }
        internal bool Enqueue(string text, string channel = null, PlayerIdentity player = null)
        {
            lock (enqueueGate)
            {
                if (disposed != 0) return false;
                foreach (var part in ChatParser.Split(text))
                {
                    if (Interlocked.Increment(ref queued) > 200) { Interlocked.Decrement(ref queued); Interlocked.Increment(ref Dropped); StatusKey = "QueueFull"; return false; }
                    queue.Enqueue(new Post { Text = part, Channel = channel, Player = player }); signal.Release();
                }
                return true;
            }
        }
        internal static string Payload(string content, string username = null, string portrait = null)
        {
            var payload = new Dictionary<string, object> {
                { "content", content }, { "allowed_mentions", new { parse = new string[0] } }, { "flags", 4 } };
            if (username != null) payload["username"] = username;
            if (portrait != null) payload["avatar_url"] = portrait;
            return new JavaScriptSerializer().Serialize(payload);
        }
        private async Task RunAsync()
        {
            var token = cancellation.Token;
            try
            {
                while (true)
                {
                    await signal.WaitAsync(token).ConfigureAwait(false);
                    Post post; if (!queue.TryDequeue(out post)) continue; Interlocked.Decrement(ref queued);
                    var portrait = await portraits.FindAsync(post.Player, token).ConfigureAwait(false);
                    var payload = Payload(post.Text, post.Player?.Display, portrait);
                    var failure = "SendFailed";
                    var delivered = false;
                    for (var attempt = 0; attempt < 4 && !delivered; attempt++)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var destination = endpoint;
                            if (hooks != null)
                            {
                                failure = "BotSetup";
                                destination = await hooks.GetAsync(post.Channel, token).ConfigureAwait(false);
                                failure = "SendFailed";
                            }
                            using (var body = new StringContent(payload, Encoding.UTF8, "application/json"))
                            using (var response = await client.PostAsync(destination, body, token).ConfigureAwait(false))
                            {
                                if (response.IsSuccessStatusCode) { Interlocked.Increment(ref Sent); StatusKey = "Sent"; delivered = true; }
                                else if ((int)response.StatusCode == 429)
                                {
                                    StatusKey = "RateLimited";
                                    var delay = RetryDelay(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                                    if (response.Headers.RetryAfter?.Delta != null) delay = Math.Max(delay, response.Headers.RetryAfter.Delta.Value.TotalSeconds);
                                    // Retain Discord's cooldown even after abandoning this message.
                                    if (delay > 300) { StatusKey = "RateLimited"; await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 86400)), token).ConfigureAwait(false); break; }
                                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delay)), token).ConfigureAwait(false);
                                }
                                else
                                {
                                    if (hooks != null && (int)response.StatusCode == 404) hooks.Forget(post.Channel);
                                    break;
                                }
                            }
                        }
                        catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; StatusKey = "SendFailed"; break; }
                        catch (BotWebhooks.Cooldown limit)
                        {
                            StatusKey = "RateLimited";
                            await Task.Delay(TimeSpan.FromSeconds(Math.Min(86400, Math.Max(1, limit.Seconds))), token).ConfigureAwait(false);
                            if (limit.Seconds > 300) break;
                        }
                        catch (Exception) { break; }
                    }
                    if (!delivered) { Interlocked.Increment(ref Dropped); StatusKey = failure; }
                    await Task.Delay(2100, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch { StatusKey = "SendFailed"; }
        }
        internal static double RetryDelay(string json)
        {
            try { var value = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); var seconds = Convert.ToDouble(value["retry_after"], CultureInfo.InvariantCulture); return double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 ? 5 : seconds; } catch { return 5; }
        }
        internal void Stop()
        {
            lock (enqueueGate)
            {
                if (disposed != 0) { }
                else { disposed = 1; cancellation.Cancel(); }
            }
            try { worker.GetAwaiter().GetResult(); }
            finally { portraits.Dispose(); client.Dispose(); signal.Dispose(); cancellation.Dispose(); }
        }
        public void Dispose() => Stop();
    }
}
