using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SkyrimOutfit
{
    internal sealed class ModOrganizerProfile
    {
        internal readonly string Name, Mods, Data, PluginList;
        internal readonly string[] EnabledMods;

        private ModOrganizerProfile(string name, string mods, string data, string profile, string overwrite)
        {
            Name = name;
            Mods = mods;
            Data = data;
            PluginList = Path.Combine(profile, "plugins.txt");
            var modList = Path.Combine(profile, "modlist.txt");
            if (!File.Exists(modList))
                throw new FileNotFoundException("MO2 profile modlist.txt was not found: " + name, modList);

            // MO2 writes the highest priority at the top of modlist.txt.
            EnabledMods = new[] { overwrite }.Concat(File.ReadLines(modList)
                .Where(line => line.StartsWith("+", StringComparison.Ordinal))
                .Select(line => line.Substring(1))
                .Where(IsFolderName)
                .Select(folder => Path.Combine(mods, folder)))
                .Where(Directory.Exists).Distinct(Files.Comparer).ToArray();
        }

        internal static ModOrganizerProfile Read(string ini)
        {
            var general = IniSection(ini, "General");
            string game = Value(general, "gameName");
            if (!String.IsNullOrEmpty(game) && !Files.Comparer.Equals(game, "Skyrim Special Edition"))
                throw new InvalidDataException("Select a Mod Organizer 2 instance for Skyrim Special Edition or Anniversary Edition.");
            string path = Value(general, "gamePath");
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new InvalidDataException("The MO2 instance does not specify a Skyrim installation.");
            string data = Path.Combine(path, "Data");
            if (!Directory.Exists(data))
                throw new DirectoryNotFoundException("Skyrim Data was not found for the selected MO2 instance: " + data);

            string instance = Path.GetDirectoryName(Path.GetFullPath(ini));
            var settings = IniSection(ini, "Settings");
            string basePath = Resolve(instance, Value(settings, "base_directory") ?? instance);
            string mods = Resolve(basePath, Value(settings, "mod_directory") ?? "mods");
            string profiles = Resolve(basePath, Value(settings, "profiles_directory") ?? "profiles");
            string overwrite = Resolve(basePath, Value(settings, "overwrite_directory") ?? "overwrite");
            string name = Value(general, "selected_profile") ?? "Default";
            if (!IsFolderName(name)) throw new InvalidDataException("Invalid MO2 profile name.");
            return new ModOrganizerProfile(name, mods, data, Path.Combine(profiles, name), overwrite);
        }

        internal string ModFor(string relative)
        {
            return EnabledMods.FirstOrDefault(folder => File.Exists(Files.PathIn(folder, relative)));
        }

        internal static string NexusId(string mod)
        {
            string meta = Path.Combine(mod, "meta.ini");
            if (!File.Exists(meta)) return null;
            try
            {
                var values = IniSection(meta, "General");
                uint id;
                return UInt32.TryParse(Value(values, "modid"), out id) && id > 0 ? id.ToString() : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static bool IsVirtualized()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                    return process.Modules.Cast<ProcessModule>().Any(module =>
                        Files.Comparer.Equals(module.ModuleName, "usvfs_x64.dll") ||
                        Files.Comparer.Equals(module.ModuleName, "usvfs_x86.dll"));
            }
            catch (Win32Exception) { return false; }
        }

        private static bool IsFolderName(string value)
        {
            return !String.IsNullOrWhiteSpace(value) && value != "." && value != ".." &&
                value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static string Resolve(string root, string value)
        {
            return Path.GetFullPath(Path.Combine(root, value.Replace("%BASE_DIR%", root).Replace('/', '\\')));
        }

        private static Dictionary<string, string> IniSection(string path, string name)
        {
            var values = new Dictionary<string, string>(Files.Comparer);
            string section = "";
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    section = line.Substring(1, line.Length - 2);
                else if (Files.Comparer.Equals(section, name))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0 && line[0] != ';' && line[0] != '#')
                        values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                }
            }
            return values;
        }

        private static string Value(Dictionary<string, string> section, string key)
        {
            string value;
            if (!section.TryGetValue(key, out value) || value.Length == 0) return null;
            if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                value = value.Substring(1, value.Length - 2);
            bool byteArray = value.StartsWith("@ByteArray(", StringComparison.Ordinal) &&
                value.EndsWith(")", StringComparison.Ordinal);
            if (byteArray) value = value.Substring(11, value.Length - 12);
            if (value.IndexOf('\\') < 0) return value;
            value = Regex.Replace(value, @"\\(\\|""|x[0-9a-fA-F]{1,2})", match =>
                match.Groups[1].Value.Length == 1 ? match.Groups[1].Value :
                    ((char)Convert.ToByte(match.Groups[1].Value.Substring(1), 16)).ToString());
            if (!byteArray || value.Any(character => character > 255)) return value;
            return new UTF8Encoding(false, false).GetString(value.Select(character => (byte)character).ToArray());
        }
    }
}
