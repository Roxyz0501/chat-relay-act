using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChatRelay
{
    internal sealed class PlayerIdentity
    {
        internal string Name, World;
        internal string Display => string.IsNullOrEmpty(World) ? Name : Name + "@" + World;
    }

    // Called under the plugin gate. Only names/worlds needed for live chat are retained.
    internal sealed class PlayerDirectory
    {
        private const string FullName = @"[A-Z][A-Za-z'-]{1,19} [A-Z][A-Za-z'-]{1,19}";
        private static readonly Regex Author = new Regex(@"^(?<name>" + FullName + @")(?:(?:\s*[@\uE05D]\s*|\s+\()(?<world>[A-Za-z]{2,24})\)?)?$");
        private static readonly Regex EmoteStart = new Regex(@"^(?<name>" + FullName + @")(?<suffix>(?:[@\uE05D][A-Za-z]+)?)(?=は|が|\s)");
        private readonly Dictionary<string, string> worlds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> knownWorlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string playerId, playerName, playerWorld;

        internal void Observe(string line)
        {
            if (line == null || line.Length > 4096) return;
            // ACT also raises timestamped colon records without the network-log checksum.
            var offset = line.Length > 15 && line[0] == '[' && line[13] == ']' ? 15 : 0;
            if (line.IndexOf('|', 0, Math.Min(4,line.Length)) < 0)
            {
                string networkType = null;
                if (At(line, offset, "AddCombatant 03:")) networkType = "03";
                else if (At(line, offset, "ChangePrimaryPlayer 02:")) networkType = "02";
                else if (At(line, offset, "Territory 01:")) networkType = "01";
                if (networkType == null) return;
                var start = line.IndexOf(':',offset);
                line = networkType + "||" + line.Substring(start+1).Replace(':','|') + "|";
            }
            if (line.StartsWith("01|", StringComparison.Ordinal)) { worlds.Clear(); return; }
            if (line.StartsWith("02|", StringComparison.Ordinal))
            {
                var f = line.Split('|');
                if (f.Length < 5) return;
                if (playerId != f[2] || playerName != f[3]) { worlds.Clear(); knownWorlds.Clear(); playerWorld = null; }
                playerId = f[2]; playerName = f[3]; return;
            }
            if (!line.StartsWith("03|", StringComparison.Ordinal) || line.Length < 5) return;
            var fields = line.Split(new[]{'|'}, 10);
            if (fields.Length < 10 || !fields[2].StartsWith("1", StringComparison.Ordinal) ||
                !Regex.IsMatch(fields[8], @"^[A-Za-z]{2,24}$") || !Author.IsMatch(fields[3])) return;
            var world = fields[8]; var name = fields[3];
            if (fields[2] == playerId) playerWorld = world;
            if (knownWorlds.Count < 256) knownWorlds.Add(world);
            string previous;
            if (worlds.TryGetValue(name, out previous)) { if (previous != world) worlds[name] = ""; }
            else if (worlds.Count < 2048) worlds.Add(name, world);
        }

        private static bool At(string value, int start, string prefix) =>
            string.CompareOrdinal(value,start,prefix,0,prefix.Length) == 0;

        internal PlayerIdentity Resolve(ChatMessage message, string defaultWorld, string actPlayerName = null)
        {
            if (message.Channel >= 56 && message.Channel <= 60) return null;
            var raw = (message.Sender ?? "").Trim();
            var self = playerName ?? actPlayerName ?? "";
            if (message.Channel == 12 || raw == "You") raw = self;
            if (raw.Length == 0 && (message.Channel == 28 || message.Channel == 29))
            {
                if (message.Text.StartsWith("You ", StringComparison.Ordinal) || message.Text.StartsWith("あなたは", StringComparison.Ordinal))
                    raw = self;
                else
                {
                    var actor = EmoteStart.Match(message.Text);
                    if (actor.Success) raw = actor.Groups["name"].Value + actor.Groups["suffix"].Value;
                }
            }
            raw = raw.TrimStart('\u2192', '\u2190', ' ', '\uE0BB');
            string suffixWorld = null;
            if (!raw.Contains("@") && !raw.Contains("\uE05D"))
            {
                // Some ACT versions flatten the world icon and concatenate the home world.
                foreach (var w in knownWorlds.Concat(new[]{defaultWorld}))
                    if (!string.IsNullOrEmpty(w) && raw.EndsWith(w, StringComparison.Ordinal) &&
                        Author.IsMatch(raw.Substring(0,raw.Length-w.Length)))
                    { suffixWorld = w; raw = raw.Substring(0,raw.Length-w.Length); break; }
            }
            var match = Author.Match(raw);
            if (!match.Success) return null;
            var name = match.Groups["name"].Value;
            var world = match.Groups["world"].Value;
            if (world.Length == 0) world = suffixWorld;
            if (string.IsNullOrEmpty(world) && !worlds.TryGetValue(name, out world))
                world = name == playerName ? playerWorld ?? defaultWorld : (string.IsNullOrEmpty(defaultWorld) ? playerWorld : defaultWorld);
            return new PlayerIdentity { Name = name, World = world };
        }
    }
}
