using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SkyrimOutfit
{
    internal sealed class OarRacePolicy
    {
        private const string Rules = "trousseRaceRules";
        private const string ResetDisabled = "trousseRaceWasDisabled";
        private const string OriginalToggle = "trousseRaceOriginalToggle";
        private const string RaceGate = "Trousse: submod race filter";
        private readonly Dictionary<string, bool> initial;
        private Dictionary<string, bool> saved;
        private readonly Dictionary<string, bool> values;
        internal bool HadReferenceOverride { get; private set; }
        internal bool Changed { get { return !Equal(initial, values); } }
        internal bool Unsaved { get { return !Equal(saved, values); } }
        internal bool HasRules { get { return values.Count != 0; } }

        internal OarRacePolicy(Dictionary<string, object> data)
        {
            var stored = Get(data, Rules) as Dictionary<string, object>;
            values = stored == null ? new Dictionary<string, bool>(Files.Comparer) : stored
                .Where(entry => entry.Value is bool && ModestyRules.RaceForms.Contains(entry.Key, Files.Comparer))
                .ToDictionary(entry => entry.Key, entry => (bool)entry.Value, Files.Comparer);
            initial = new Dictionary<string, bool>(values, Files.Comparer);
            MarkSaved();
            data.Remove(Rules);
            if (data.Remove(ResetDisabled)) data["disabled"] = true;
            var conditions = Get(data, "conditions") as IList;
            if (conditions != null)
                data["conditions"] = conditions.Cast<object>().Where(condition => !IsGate(condition)).ToArray();
            HadReferenceOverride = RestoreReferences(data);
        }

        internal string Choice(string race)
        {
            bool enabled;
            return values.TryGetValue(race, out enabled) ? enabled ? "on" : "off" : "inherit";
        }

        internal void Set(string race, string value)
        {
            if (value == "on" || value == "off") values[race] = value == "on";
            else values.Remove(race);
        }

        internal void MarkSaved() { saved = new Dictionary<string, bool>(values, Files.Comparer); }

        internal void Apply(Dictionary<string, object> data, bool isToggle)
        {
            if (!HasRules) return;
            data[Rules] = values;
            if (isToggle) return;
            bool globallyDisabled = Equals(Get(data, "disabled"), true);
            var races = values.Where(entry => entry.Value == globallyDisabled).Select(entry => Race(entry.Key)).ToArray();
            if (races.Length == 0) return;
            var gate = Combine("OR", races);
            if (!globallyDisabled) gate["negated"] = true;
            gate["comment"] = RaceGate;
            var conditions = Get(data, "conditions") as IList;
            data["conditions"] = (conditions == null ? Enumerable.Empty<object>() : conditions.Cast<object>()).Concat(new[] { gate }).ToArray();
            if (globallyDisabled)
            {
                data[ResetDisabled] = true;
                data.Remove("disabled");
            }
        }

        internal static bool PatchReferences(object node, string moduleName, Dictionary<string, OarRacePolicy> switches)
        {
            var map = node as Dictionary<string, object>;
            if (map != null)
            {
                bool modified = false;
                foreach (var entry in map.ToArray())
                {
                    if (entry.Key == "trousseOriginalCondition") continue;
                    var condition = entry.Value as Dictionary<string, object>;
                    var wrapped = condition == null ? null : WrapToggle(condition, moduleName, switches);
                    if (wrapped != null) { map[entry.Key] = wrapped; modified = true; }
                    else modified |= PatchReferences(entry.Value, moduleName, switches);
                }
                return modified;
            }
            var list = node as IList;
            if (list == null) return false;
            bool changed = false;
            for (int index = 0; index < list.Count; index++)
            {
                var condition = list[index] as Dictionary<string, object>;
                var wrapped = condition == null ? null : WrapToggle(condition, moduleName, switches);
                if (wrapped != null) { list[index] = wrapped; changed = true; }
                else changed |= PatchReferences(list[index], moduleName, switches);
            }
            return changed;
        }

        private static Dictionary<string, object> WrapToggle(Dictionary<string, object> condition, string moduleName,
            Dictionary<string, OarRacePolicy> switches)
        {
            if (!Equals(Get(condition, "condition"), "IsReplacerEnabled") ||
                !Files.Comparer.Equals(Get(condition, "Replacer mod name") as string, moduleName)) return null;
            string name = Get(condition, "Submod name") as string;
            OarRacePolicy policy;
            if (name == null || !switches.TryGetValue(name, out policy) || !policy.HasRules) return null;
            var gate = new Dictionary<string, object>(condition);
            string[] outer = { "disabled", "negated", "comment" };
            foreach (string key in outer) gate.Remove(key);
            var inherited = Combine("OR", policy.values.Keys.Select(Race));
            inherited["negated"] = true;
            var modes = new[] { Combine("AND", new[] { gate, inherited }) }.Concat(
                policy.values.Where(entry => entry.Value).Select(entry => Race(entry.Key)));
            var result = Combine("OR", modes);
            foreach (string key in outer)
            {
                object value;
                if (condition.TryGetValue(key, out value)) result[key] = value;
            }
            result[OriginalToggle] = condition;
            return result;
        }

        private static bool RestoreReferences(object node)
        {
            var map = node as Dictionary<string, object>;
            if (map != null)
            {
                bool changed = false;
                foreach (var entry in map.ToArray())
                {
                    var wrapped = entry.Value as Dictionary<string, object>;
                    object old = wrapped == null ? null : Get(wrapped, OriginalToggle);
                    if (old is Dictionary<string, object>) { map[entry.Key] = old; changed = true; }
                    else changed |= RestoreReferences(entry.Value);
                }
                return changed;
            }
            var list = node as IList;
            if (list == null) return false;
            bool restored = false;
            for (int index = 0; index < list.Count; index++)
            {
                var wrapped = list[index] as Dictionary<string, object>;
                object old = wrapped == null ? null : Get(wrapped, OriginalToggle);
                if (old is Dictionary<string, object>) { list[index] = old; restored = true; }
                else restored |= RestoreReferences(list[index]);
            }
            return restored;
        }

        private static bool IsGate(object entry)
        {
            var map = entry as Dictionary<string, object>;
            return map != null && Equals(Get(map, "comment"), RaceGate);
        }

        private static bool Equal(Dictionary<string, bool> left, Dictionary<string, bool> right)
        {
            return left.Count == right.Count && left.All(pair => right.ContainsKey(pair.Key) && right[pair.Key] == pair.Value);
        }

        private static object Get(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> Race(string form)
        {
            return new Dictionary<string, object> {
                { "condition", "IsRace" }, { "requiredVersion", "1.0.0.0" },
                { "Race", new Dictionary<string, object> { { "pluginName", "Skyrim.esm" }, { "formID", form } } }
            };
        }

        private static Dictionary<string, object> Combine(string op, IEnumerable<Dictionary<string, object>> conditions)
        {
            return new Dictionary<string, object> { { "condition", op }, { "requiredVersion", "1.0.0.0" }, { "Conditions", conditions.ToArray() } };
        }
    }
}
