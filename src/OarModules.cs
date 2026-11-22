using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace SkyrimOutfit
{
    internal sealed class OarModule
    {
        public string Name { get; private set; }
        internal readonly string Folder;
        internal readonly bool Ini;
        internal OarSettings Settings;
        internal OarDocument[] Documents;
        internal readonly List<string> Unreadable = new List<string>();

        internal OarModule(string name, string folder, bool ini)
        {
            Name = name;
            Folder = folder;
            Ini = ini;
        }

        internal static OarModule[] Scan(string data)
        {
            var result = new List<OarModule>();
            foreach (string path in Files.Children(Path.Combine(data, @"SKSE\Plugins"), "OpenAnimationReplacer*.ini", false)
                .Where(file => !Files.Comparer.Equals(Path.GetFileName(file), "OpenAnimationReplacer.ini") &&
                    !Files.Comparer.Equals(Path.GetFileName(file), "OpenAnimationReplacer_ImGui.ini")))
                result.Add(new OarModule(Path.GetFileNameWithoutExtension(path), path, true));

            string actors = Path.Combine(data, @"Meshes\actors");
            var pending = new Stack<string>();
            if (Directory.Exists(actors)) pending.Push(actors);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                foreach (string folder in Files.Children(dir, "*", true))
                {
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                    if (Files.Comparer.Equals(Path.GetFileName(folder), "OpenAnimationReplacer"))
                    {
                        foreach (string mod in Files.Children(folder, "*", true))
                        {
                            if (!OarDocument.Exists(mod) && !Files.Children(mod, "*", true).Any(OarDocument.Exists)) continue;
                            string name = OarDocument.ReadName(mod);
                            result.Add(new OarModule(name, mod, false));
                        }
                    }
                    else pending.Push(folder);
                }
            }
            return result.OrderByDescending(module => module.Name.IndexOf("modesty", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(module => module.Name, Files.Comparer).ThenBy(module => module.Folder, Files.Comparer).ToArray();
        }

        internal OarDocument[] Open()
        {
            if (Documents != null) return Documents;
            var files = new List<OarDocument>();
            foreach (string folder in new[] { Folder }.Concat(Files.Children(Folder, "*", true)).Where(OarDocument.Exists))
            {
                try { files.Add(new OarDocument(folder, Files.Comparer.Equals(folder, Folder))); }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)) throw;
                    Unreadable.Add(Path.GetFileName(folder) + " · " + error.Message);
                }
            }
            Documents = files.OrderByDescending(file => file.IsMod).ThenByDescending(file => file.ToggleName != null)
                .ThenBy(file => file.Name, Files.Comparer).ToArray();
            return Documents;
        }

        internal static void Export(string destination, string data, IEnumerable<OarModule> modules)
        {
            var files = new Dictionary<string, string>(Files.Comparer);
            var edited = modules.Where(module => module.Documents != null && module.Documents.Any(document => document.IsChanged)).ToArray();
            foreach (var module in edited)
            {
                var switches = module.Documents.Where(document => document.ToggleName != null && document.Race.HasRules)
                    .GroupBy(document => document.Name, Files.Comparer)
                    .ToDictionary(group => group.Key, group => group.First().Race, Files.Comparer);
                foreach (var document in module.Documents)
                {
                    if (!document.IsChanged && !document.Race.HadReferenceOverride && switches.Count == 0) continue;
                    var payload = document.Snapshot();
                    document.Race.Apply(payload, document.ToggleName != null);
                    bool rewritten = OarRacePolicy.PatchReferences(payload, module.Name, switches);
                    if (document.IsChanged || rewritten || document.Race.HadReferenceOverride)
                        files.Add(document.FilePath, OarDocument.Serialize(payload));
                }
            }
            if (files.Count == 0) throw new InvalidOperationException("No OAR configuration has changed.");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Open(temporary, FileMode.CreateNew))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    foreach (var file in files)
                    {
                        string relative = Files.Relative(data, file.Key);
                        Files.PathIn(data, relative);
                        using (var writer = new StreamWriter(zip.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
                            writer.Write(file.Value);
                    }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
                foreach (var document in edited.SelectMany(module => module.Documents)) document.MarkSaved();
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal sealed class OarDocument
    {
        private readonly Dictionary<string, object> data;
        private readonly List<JsonOption> options = new List<JsonOption>();
        private static readonly Dictionary<string, object> SubmodDefaults = new Dictionary<string, object> {
            { "disabled", false }, { "priority", 0 }, { "interruptible", false },
            { "replaceOnLoop", true }, { "replaceOnEcho", false },
            { "runFunctionsOnLoop", true }, { "runFunctionsOnEcho", true },
            { "hasCustomBlendTimeOnInterrupt", false }, { "blendTimeOnInterrupt", 0.3m },
            { "hasCustomBlendTimeOnLoop", false }, { "blendTimeOnLoop", 0.3m },
            { "hasCustomBlendTimeOnEcho", false }, { "blendTimeOnEcho", 0.1m },
            { "ignoreDontConvertAnnotationsToTriggersFlag", false }, { "triggersFromAnnotationsOnly", false },
            { "overrideAnimationsFolder", "" }, { "requiredBehaviorProjectName", "" }
        };

        public string Name { get; private set; }
        internal readonly string Description, FilePath, ToggleName;
        internal readonly bool IsMod;
        internal readonly OarRacePolicy Race;
        internal bool IsChanged { get { return Race.Changed || options.Any(option => option.IsChanged); } }
        internal bool IsUnsaved { get { return Race.Unsaved || options.Any(option => option.IsUnsaved); } }

        internal OarDocument(string folder, bool mod)
        {
            string config = Path.Combine(folder, "config.json");
            FilePath = Path.Combine(folder, "user.json");
            var author = File.Exists(config) ? Read(config) : new Dictionary<string, object>();
            var user = File.Exists(FilePath) ? Read(FilePath) : null;
            // OAR loads behavior from user.json if it exists, with title and description from config.json.
            data = user ?? author;
            Race = new OarRacePolicy(data);
            Name = Text(author, "name") ?? Text(data, "name") ?? Path.GetFileName(folder);
            Description = Text(author, "description") ?? Text(data, "description") ?? "";
            IsMod = mod;
            if (!mod && Name.IndexOf("ON/OFF", StringComparison.OrdinalIgnoreCase) >= 0)
                ToggleName = Name.Replace("ON/OFF", "").Trim();
            if (!mod)
                foreach (var setting in SubmodDefaults)
                    if (!data.ContainsKey(setting.Key)) Add("General", data, setting.Key, setting.Value, false);
            Walk(data, "General");
        }

        internal static bool Exists(string folder)
        {
            return File.Exists(Path.Combine(folder, "config.json")) || File.Exists(Path.Combine(folder, "user.json"));
        }

        internal static string ReadName(string folder)
        {
            try
            {
                foreach (string filename in new[] { "config.json", "user.json" })
                {
                    string path = Path.Combine(folder, filename);
                    if (File.Exists(path))
                    {
                        string name = Text(Read(path), "name");
                        if (!String.IsNullOrWhiteSpace(name)) return name;
                    }
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)) throw;
            }
            return Path.GetFileName(folder);
        }

        private static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = 24000000, RecursionLimit = 128 };
        }

        private static Dictionary<string, object> Read(string path)
        {
            var result = Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (result == null) throw new InvalidDataException("Invalid OAR config: " + path);
            return result;
        }

        private static string Text(IDictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) ? value as string : null;
        }

        private void Add(string context, Dictionary<string, object> map, string key, object value, bool exists)
        {
            options.Add(new JsonOption(context, key, value, dataValue => map[key] = dataValue,
                () => { if (exists) map[key] = value; else map.Remove(key); }));
        }

        private void Walk(Dictionary<string, object> map, string context)
        {
            bool condition = Text(map, "condition") != null;
            if ((condition || Text(map, "filename") != null || Text(map, "projectName") != null) && !map.ContainsKey("disabled"))
                Add(context, map, "disabled", false, false);
            if (condition && !map.ContainsKey("negated")) Add(context, map, "negated", false, false);
            foreach (var entry in map)
            {
                string key = entry.Key;
                if (key == "trousseOriginalCondition" || key == "condition" || key == "requiredVersion"
                    || key == "requiredPlugin" || key == "function" || key == "name" || key == "author"
                    || key == "description" || key == "projectName" || key == "path" || key == "filename") continue;
                var nested = entry.Value as Dictionary<string, object>;
                if (nested != null) Walk(nested, context + " / " + OarOptions.HumanName(key));
                else if (entry.Value is IList)
                {
                    var array = (IList)entry.Value;
                    for (int i = 0; i < array.Count; i++)
                    {
                        var child = array[i] as Dictionary<string, object>;
                        string name = child == null ? null : Text(child, "name") ?? Text(child, "condition") ?? Text(child, "function") ??
                            Text(child, "filename") ?? Text(child, "path");
                        string group = context == "General" ? key : context;
                        string caption = group + " / " + (i + 1) + ". " + (name ?? OarOptions.HumanName(key));
                        if (child != null) Walk(child, caption);
                        else if (IsScalar(array[i]))
                        {
                            int position = i;
                            object value = array[i];
                            options.Add(new JsonOption(group, caption, value, replacement => array[position] = replacement, () => array[position] = value));
                        }
                    }
                }
                else if (IsScalar(entry.Value)) Add(context, map, key, entry.Value, true);
            }
        }

        private static bool IsScalar(object value)
        {
            return value is bool || value is string || value is int || value is long || value is decimal || value is double;
        }

        internal OarField[] Fields(bool italian)
        {
            return options.OrderBy(option => option.Context != "General").ThenBy(option => option.Key != "disabled")
                .Select(option => option.Field(italian, option.Context == "General" ? ToggleName : null)).ToArray();
        }

        internal string Serialize() { return Serialize(Snapshot()); }
        internal static string Serialize(Dictionary<string, object> payload) { return Json().Serialize(payload); }

        internal Dictionary<string, object> Snapshot()
        {
            var changed = options.Where(option => option.IsChanged).ToArray();
            try
            {
                foreach (var option in changed) option.Apply();
                var serializer = Json();
                return serializer.Deserialize<Dictionary<string, object>>(serializer.Serialize(data));
            }
            finally { foreach (var option in changed) option.ResetObject(); }
        }

        internal void MarkSaved()
        {
            Race.MarkSaved();
            foreach (var option in options) option.MarkSaved();
        }

        private sealed class JsonOption
        {
            internal readonly string Context, Key;
            private readonly object original;
            private readonly string initial;
            private string value, saved;
            private readonly Action<object> set;
            internal readonly Action ResetObject;

            internal JsonOption(string context, string key, object original, Action<object> set, Action reset)
            {
                Context = context;
                Key = key;
                this.original = original;
                this.set = set;
                ResetObject = reset;
                initial = saved = value = original is bool ? ((bool)original ? "true" : "false")
                    : Convert.ToString(original, CultureInfo.InvariantCulture);
            }

            internal bool IsChanged { get { return initial != value; } }
            internal bool IsUnsaved { get { return saved != value; } }
            internal void MarkSaved() { saved = value; }

            internal OarField Field(bool italian, string toggleName)
            {
                OarChoice[] choices = Key == "Comparison" && original is string
                    ? new[] { "==", "!=", "<", "<=", ">", ">=" }.Select(symbol => new OarChoice(symbol, symbol)).ToArray() : null;
                if (Key == "variantMode" && original is int)
                    choices = new[] { new OarChoice("0", italian ? "Casuale" : "Random"), new OarChoice("1", italian ? "Sequenziale" : "Sequential") };
                bool inverse = Key == "disabled" && original is bool;
                var option = new OarField(() => value, input => value = input,
                    original is bool ? "Flag" : choices != null ? "Choice" : "Text",
                    inverse, OarOptions.IncludeCurrent(choices, value, italian));
                option.Label = inverse ? (toggleName == null ? (italian ? "Attivo" : "Enabled") : ToggleLabel(toggleName, italian))
                    : Key == "priority" ? (italian ? "Priorità" : "Priority")
                    : Key == "negated" ? (italian ? "Inverti condizione" : "Invert condition")
                    : Key == "Comparison" ? (italian ? "Confronto" : "Comparison")
                    : Key == "variantMode" ? (italian ? "Ordine delle varianti" : "Variant playback order")
                    : OarOptions.HumanName(Key);
                option.Context = Context == "General" ? OarOptions.Section("General", italian) : Context;
                option.Hint = Key + " · " + option.Context;
                return option;
            }

            private static string ToggleLabel(string name, bool italian)
            {
                if (!italian) return name;
                string actor = name.Contains("NPC") ? "PNG" : "PG";
                if (name.Contains("Detection")) return "Rilevamento degli osservatori · " + actor;
                if (name.Contains("Sneaking")) return "Modestia in furtività · " + actor;
                if (name.Contains("Weapon")) return "Modestia con armi e magia · " + actor;
                if (name.Contains("Shield")) return "Scudo sulla schiena · " + actor;
                return name.Contains("Modesty") ? "Animazioni di modestia · " + actor : name;
            }

            internal void Apply()
            {
                try
                {
                    string input = (value ?? "").Trim();
                    if (!(original is string) && (original is decimal || original is double)) input = input.Replace(',', '.');
                    object result = original is string ? (value ?? "") : Convert.ChangeType(input, original.GetType(), CultureInfo.InvariantCulture);
                    if (result is double && (Double.IsNaN((double)result) || Double.IsInfinity((double)result))) throw new FormatException();
                    set(result);
                }
                catch (Exception error)
                {
                    if (!(error is FormatException || error is OverflowException)) throw;
                    throw new InvalidDataException(Key + " (" + Context + "): invalid value.");
                }
            }
        }
    }
}
