using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChatRelay
{
    internal sealed class ChatMessage { public int Channel; public string Sender; public string Text; }
    internal static class ChatParser
    {
        internal static readonly int[] Channels = new[] { 10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,27,30,32,36,37,101,102,103,104,105,106,107,56 };
        internal static bool IsSupported(int channel) => Channels.Contains(channel);
        internal static string ChannelKey(int channel) => channel >= 16 && channel <= 23 ? "LS " + (channel - 15) : channel >= 101 && channel <= 107 ? "CWLS " + (channel - 99) : channel == 37 ? "CWLS 1" : "Channel" + channel;
        internal static bool TryParse(string line, out ChatMessage message)
        {
            message = null; if (string.IsNullOrEmpty(line) || line.Length > 32768) return false;
            string code, sender, body;
            if (line.StartsWith("00|", StringComparison.Ordinal))
            {
                var fields = line.Split('|'); if (fields.Length < 6) return false;
                code = fields[2]; sender = fields[3];
                // Network logs append a checksum. Preserve any delimiters in the message.
                body = string.Join("|", fields.Skip(4).Take(fields.Length - 5));
            }
            else
            {
                var match = Regex.Match(line, @"^(?:\[\d{2}:\d{2}:\d{2}\.\d{3}\] )?(?:ChatLog )?00:([0-9A-Fa-f]{4}):([^:]*):(.*)$");
                if (!match.Success) return false; code = match.Groups[1].Value; sender = match.Groups[2].Value; body = match.Groups[3].Value;
            }
            int raw; if (code.Length != 4 || !int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out raw)) return false;
            var channel = raw & 0x7f;
            if (!IsSupported(channel) || string.IsNullOrWhiteSpace(body)) return false;
            message = new ChatMessage { Channel = channel, Sender = sender, Text = body }; return true;
        }
        internal static string Format(ChatMessage message, string language, bool names)
        {
            var text = "[" + Localization.Get(language, ChannelKey(message.Channel)) + "] " + (names && !string.IsNullOrEmpty(message.Sender) ? message.Sender + ": " : "") + message.Text;
            return Regex.Replace(text, @"([\\`*_{}\[\]()<>~|])", @"\$1");
        }
        internal static IEnumerable<string> Split(string text)
        {
            for (int offset = 0; offset < text.Length;) { var count = Math.Min(1900, text.Length - offset); if (offset + count < text.Length && char.IsHighSurrogate(text[offset + count - 1])) count--; yield return text.Substring(offset, count); offset += count; }
        }
    }
}
