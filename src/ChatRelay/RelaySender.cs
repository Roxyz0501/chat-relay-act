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
        private readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly HttpClient client;
        private readonly Uri endpoint;
        private readonly Task worker;
        private int queued, disposed;
        internal string StatusKey = "Ready";
        internal int Sent, Dropped;
        internal RelaySender(Uri url, HttpMessageHandler handler = null)
        {
            endpoint = new Uri(url.GetLeftPart(UriPartial.Path) + "?wait=true");
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
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
        internal bool Enqueue(string text)
        {
            if (Volatile.Read(ref disposed) != 0) return false;
            foreach (var part in ChatParser.Split(text))
            {
                if (Interlocked.Increment(ref queued) > 200) { Interlocked.Decrement(ref queued); Interlocked.Increment(ref Dropped); StatusKey = "QueueFull"; return false; }
                queue.Enqueue(part); signal.Release();
            }
            return true;
        }
        internal static string Payload(string content) => new JavaScriptSerializer().Serialize(new { content, allowed_mentions = new { parse = new string[0] }, flags = 4 });
        private async Task RunAsync()
        {
            var token = cancellation.Token;
            try
            {
                while (true)
                {
                    await signal.WaitAsync(token).ConfigureAwait(false);
                    string text; if (!queue.TryDequeue(out text)) continue; Interlocked.Decrement(ref queued);
                    var delivered = false;
                    for (var attempt = 0; attempt < 4 && !delivered; attempt++)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            using (var body = new StringContent(Payload(text), Encoding.UTF8, "application/json"))
                            using (var response = await client.PostAsync(endpoint, body, token).ConfigureAwait(false))
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
                                else { StatusKey = "SendFailed"; break; }
                            }
                        }
                        catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; StatusKey = "SendFailed"; break; }
                        catch (HttpRequestException) { StatusKey = "SendFailed"; break; }
                    }
                    if (!delivered) { Interlocked.Increment(ref Dropped); StatusKey = "SendFailed"; }
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
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            cancellation.Cancel();
            // Dispose after the worker has unwound; never block ACT's UI thread.
            worker.ContinueWith(_ => { client.Dispose(); cancellation.Dispose(); });
        }
    }
}
