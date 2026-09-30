// my-rekkr dev7 — save backup: all save slots (doomsav0..9.dsg) + the app settings in one file that the
// user stores anywhere (Android document picker, see Plugins/Android/RekkrDocs.java), and the way back.
// Reason: the 0.6.0 key change forced an uninstall, which deleted every save.
// Format (plain, versioned): "REKKR-BACKUP 1\n" then entries "F <name> <length>\n<bytes>\n", the settings as
// entry "settings.txt" with lines "i <key> <int>" / "s <key> <base64 utf-8>".
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ManagedDoom.UnityPort
{
    public static class SaveBackup
    {
        public const string Magic = "REKKR-BACKUP 1";
        private static readonly Regex SaveName = new Regex(@"^doomsav[0-9]\.dsg$");

        /// <summary>Builds the backup bytes from the save directory and a settings snapshot.</summary>
        public static byte[] Pack(string saveDir, IDictionary<string, object> prefs, out int saves)
        {
            saves = 0;
            using (var ms = new MemoryStream())
            {
                void Entry(string name, byte[] data)
                {
                    var head = Encoding.ASCII.GetBytes($"F {name} {data.Length}\n");
                    ms.Write(head, 0, head.Length);
                    ms.Write(data, 0, data.Length);
                    ms.WriteByte((byte)'\n');
                }
                var magic = Encoding.ASCII.GetBytes(Magic + "\n");
                ms.Write(magic, 0, magic.Length);
                for (var i = 0; i <= 9; i++)
                {
                    var p = Path.Combine(saveDir, "doomsav" + i + ".dsg");
                    if (!File.Exists(p)) continue;
                    Entry("doomsav" + i + ".dsg", File.ReadAllBytes(p));
                    saves++;
                }
                var sb = new StringBuilder();
                foreach (var kv in prefs)
                {
                    if (kv.Value is int iv) sb.Append("i ").Append(kv.Key).Append(' ').Append(iv).Append('\n');
                    else if (kv.Value is string sv) sb.Append("s ").Append(kv.Key).Append(' ').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(sv))).Append('\n');
                }
                Entry("settings.txt", Encoding.UTF8.GetBytes(sb.ToString()));
                return ms.ToArray();
            }
        }

        /// <summary>Parses a backup. Returns null (and a reason) when the bytes are not a REKKR backup.</summary>
        public static Dictionary<string, byte[]> Unpack(byte[] data, out string error)
        {
            error = null;
            var files = new Dictionary<string, byte[]>();
            var pos = 0;
            string Line()
            {
                var nl = Array.IndexOf(data, (byte)'\n', pos);
                if (nl < 0 || nl - pos > 200) return null;
                var s = Encoding.ASCII.GetString(data, pos, nl - pos);
                pos = nl + 1;
                return s;
            }
            if (data == null || data.Length < Magic.Length || Line() != Magic) { error = "not a REKKR backup"; return null; }
            while (pos < data.Length)
            {
                var head = Line();
                if (head == null) { error = "broken entry header"; return null; }
                var f = head.Split(' ');
                if (f.Length != 3 || f[0] != "F" || !int.TryParse(f[2], out var len) || len < 0 || pos + len > data.Length) { error = "broken entry " + head; return null; }
                var bytes = new byte[len];
                Buffer.BlockCopy(data, pos, bytes, 0, len);
                pos += len + 1;   // + '\n'
                files[f[1]] = bytes;
            }
            if (!files.ContainsKey("settings.txt")) { error = "no settings entry"; return null; }
            return files;
        }

        /// <summary>Writes the save slots of a parsed backup into <paramref name="saveDir"/> (only doomsavN.dsg
        /// names; existing slots with the same number are replaced) and returns the settings.</summary>
        public static Dictionary<string, object> Apply(Dictionary<string, byte[]> files, string saveDir, out int saves)
        {
            saves = 0;
            foreach (var kv in files)
            {
                if (!SaveName.IsMatch(kv.Key)) continue;
                File.WriteAllBytes(Path.Combine(saveDir, kv.Key), kv.Value);
                saves++;
            }
            var prefs = new Dictionary<string, object>();
            foreach (var line in Encoding.UTF8.GetString(files["settings.txt"]).Split('\n'))
            {
                var p = line.Split(' ');
                if (p.Length != 3) continue;
                if (p[0] == "i" && int.TryParse(p[2], out var iv)) prefs[p[1]] = iv;
                else if (p[0] == "s")
                {
                    try { prefs[p[1]] = Encoding.UTF8.GetString(Convert.FromBase64String(p[2])); } catch (FormatException) { }
                }
            }
            return prefs;
        }
    }
}
