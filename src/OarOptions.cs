using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SkyrimOutfit
{
    internal sealed class OarChoice
    {
        public string Value { get; private set; }
        public string Label { get; private set; }
        internal OarChoice(string value, string label) { Value = value; Label = label; }
    }

    internal sealed class OarField
    {
        private readonly bool inverse;
        private readonly string enabled, disabled;
        private readonly Func<string> read;
        private readonly Action<string> write;
        public string Label { get; internal set; }
        public string Context { get; internal set; }
        public string Hint { get; internal set; }
        public string Kind { get; private set; }
        public OarChoice[] Choices { get; private set; }
        public string Value { get { return read(); } set { if (value != null) write(value); } }
        public bool Checked
        {
            get { return OarOptions.IsOn(Value) != inverse; }
            set { if (value != Checked) Value = value != inverse ? enabled : disabled; }
        }

        internal OarField(Func<string> read, Action<string> write, string kind, bool inverse = false, OarChoice[] choices = null)
        {
            this.read = read;
            this.write = write;
            this.inverse = inverse;
            string original = read().ToLowerInvariant();
            if (original == "0" || original == "1") { enabled = "1"; disabled = "0"; }
            else if (original == "yes" || original == "no") { enabled = "yes"; disabled = "no"; }
            else if (original == "on" || original == "off") { enabled = "on"; disabled = "off"; }
            else { enabled = "true"; disabled = "false"; }
            Kind = kind;
            Choices = choices;
        }
    }

    internal static class OarOptions
    {
        internal static bool IsOn(string value)
        {
            return value == "1" || Files.Comparer.Equals(value, "true") || Files.Comparer.Equals(value, "yes") || Files.Comparer.Equals(value, "on");
        }

        internal static bool IsFlag(string value)
        {
            return IsOn(value) || value == "0" || Files.Comparer.Equals(value, "false") ||
                Files.Comparer.Equals(value, "no") || Files.Comparer.Equals(value, "off");
        }
        // English and Italian show the meaning of each setting, with its INI key in the tooltip.
        internal static readonly string[] Global = {
            "General|uAnimationLimit|32767|Animation limit|Limite animazioni|Maximum registered animations|Numero massimo di animazioni registrate",
            "General|uHavokHeapSize|1073741824|Havok memory|Memoria Havok|Memory allocation in bytes|Memoria allocata in byte",
            "General|bAsyncParsing|true|Parse animations in the background|Analizza le animazioni in background",
            "General|bLoadDefaultBehaviorsInMainMenu|true|Load default behaviors at the main menu|Carica i comportamenti predefiniti nel menu principale",
            "General|uParsingWorkerCount|4|Animation parsing threads|Thread per l’analisi delle animazioni",
            "Filtering|bFilterOutDuplicateAnimations|true|Filter duplicate animations|Filtra animazioni duplicate",
            "Filtering|bVerifyDuplicateHashesByComparingFileBytes|false|Verify duplicate files byte by byte|Verifica i duplicati byte per byte",
            "UI|bEnableUI|true|Enable the in-game editor|Abilita l’editor in gioco",
            "UI|bShowWelcomeBanner|true|Show the welcome message|Mostra il messaggio di benvenuto",
            "UI|uToggleUIKey|24|Editor shortcut key|Tasto di apertura dell’editor",
            "UI|uToggleUIKeyCtrl|0|Require Ctrl for the shortcut|Richiedi Ctrl per la scorciatoia",
            "UI|uToggleUIKeyShift|1|Require Shift for the shortcut|Richiedi Maiusc per la scorciatoia",
            "UI|uToggleUIKeyAlt|0|Require Alt for the shortcut|Richiedi Alt per la scorciatoia",
            "UI|fUIScale|1.0|Interface scale|Scala dell’interfaccia|1 is 100%|1 equivale al 100%",
            "UI|bEnableAnimationQueueProgressBar|true|Show animation loading progress|Mostra l’avanzamento del caricamento",
            "UI|fAnimationQueueLingerTime|5.0|Keep the loading bar visible (seconds)|Mantieni visibile la barra (secondi)",
            "UI|bEnableAnimationLog|false|Enable the animation log|Abilita il registro animazioni",
            "UI|fAnimationLogWidth|650.0|Animation log width (px)|Larghezza registro animazioni (px)",
            "UI|uAnimationLogSize|10|Maximum animation log entries|Numero massimo di voci nel registro",
            "UI|uAnimationActivateLogMode|2|Log newly started animations|Registra l’avvio delle animazioni",
            "UI|uAnimationEchoLogMode|0|Log animation echoes|Registra gli echo delle animazioni",
            "UI|uAnimationLoopLogMode|0|Log animation loops|Registra le ripetizioni delle animazioni",
            "UI|bAnimationLogOnlyActiveGraph|true|Log only the active behavior graph|Registra solo il grafo di comportamento attivo",
            "UI|bAnimationLogWriteToTextLog|false|Write the animation log to a file|Scrivi il registro animazioni su file",
            "UI|bEnableAnimationEventLog|false|Enable the animation event log|Abilita il registro eventi delle animazioni",
            "UI|bAnimationLogOrderDescending|true|Show newest log entries first|Mostra prima le voci più recenti",
            "UI|fAnimationEventLogWidth|500.0|Event log width (px)|Larghezza registro eventi (px)",
            "UI|fAnimationEventLogHeight|600.0|Event log height (px)|Altezza registro eventi (px)",
            "UI|bAnimationEventLogWriteToTextLog|false|Write the event log to a file|Scrivi il registro eventi su file",
            "UI|fAnimationLogsOffsetX|0.0|Horizontal log offset (px)|Spostamento orizzontale dei registri (px)",
            "UI|fAnimationLogsOffsetY|30.0|Vertical log offset (px)|Spostamento verticale dei registri (px)",
            "Workarounds|bLegacyKeepRandomResultsByDefault|true|Keep random results for legacy animations|Mantieni i risultati casuali per le animazioni legacy",
            "Experimental|bDisablePreloading|false|Disable animation preloading|Disattiva il precaricamento delle animazioni",
            "Experimental|bIncreaseAnimationLimit|false|Allow a higher animation limit|Consenti un limite di animazioni più alto",
            "Debug|bEnableDebugDraws|false|Show debug shapes|Mostra le forme di debug"
        };

        internal static string Section(string key, bool italian)
        {
            if (key == "") return italian ? "Senza sezione" : "Ungrouped";
            if (!italian) return key;
            switch (key)
            {
                case "General": return "Generali";
                case "Filtering": return "Filtri";
                case "UI": return "Interfaccia";
                case "Workarounds": return "Compatibilità";
                case "Experimental": return "Sperimentali";
                case "Tweaks": return "Regolazioni";
                default: return key;
            }
        }

        internal static string HumanName(string key)
        {
            return Regex.Replace(key.Replace('_', ' '), @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        }

        internal static OarField IniField(OarSettings.Entry entry, bool italian, bool global)
        {
            string key = entry.Key;
            string[] description = global ? Global.Select(row => row.Split('|'))
                .FirstOrDefault(row => Files.Comparer.Equals(row[0], entry.Section) && Files.Comparer.Equals(row[1], key)) : null;
            var logModes = new[] { italian ? "Solo sostituzioni" : "Replacements only",
                italian ? "Possibili sostituzioni" : "Potential replacements", italian ? "Tutte" : "All animations" };
            OarChoice[] choices = !global ? null : Regex.IsMatch(key, @"^uAnimation(?:Activate|Echo|Loop)LogMode$")
                ? logModes.Select((label, index) => new OarChoice(index.ToString(CultureInfo.InvariantCulture), label)).ToArray()
                : key == "uToggleUIKey" ? ShortcutKeys() : null;
            bool checkbox = IsFlag(entry.Value) &&
                (entry.Value != "0" && entry.Value != "1" || key.StartsWith("b", StringComparison.Ordinal) ||
                    global && Regex.IsMatch(key, @"^uToggleUIKey(?:Ctrl|Shift|Alt)$"));
            var field = new OarField(() => entry.Value, value => entry.Value = value,
                choices != null ? "Choice" : checkbox ? "Flag" : "Text", false, IncludeCurrent(choices, entry.Value, italian));
            field.Label = description == null ? HumanName(Regex.Replace(key, @"^[buf](?=[A-Z])", "")) : description[italian ? 4 : 3];
            field.Context = Section(entry.Section, italian);
            field.Hint = description != null && description.Length > 5 ? description[italian ? 6 : 5] : entry.Comment;
            field.Hint = key + (String.IsNullOrWhiteSpace(field.Hint) ? "" : Environment.NewLine + field.Hint);
            return field;
        }

        internal static OarChoice[] IncludeCurrent(OarChoice[] choices, string current, bool italian)
        {
            if (choices == null || choices.Any(choice => choice.Value == current)) return choices;
            return choices.Concat(new[] { new OarChoice(current, (italian ? "Valore attuale · " : "Current value · ") + current) }).ToArray();
        }

        private static OarChoice[] ShortcutKeys()
        {
            // DirectInput scan codes used by the SKSE input event.
            string[] rows = {
                "1:Esc", "2:1", "3:2", "4:3", "5:4", "6:5", "7:6", "8:7", "9:8", "10:9", "11:0",
                "14:Backspace", "15:Tab", "16:Q", "17:W", "18:E", "19:R", "20:T", "21:Y", "22:U", "23:I", "24:O", "25:P",
                "28:Enter", "30:A", "31:S", "32:D", "33:F", "34:G", "35:H", "36:J", "37:K", "38:L",
                "44:Z", "45:X", "46:C", "47:V", "48:B", "49:N", "50:M", "57:Space",
                "59:F1", "60:F2", "61:F3", "62:F4", "63:F5", "64:F6", "65:F7", "66:F8", "67:F9", "68:F10", "87:F11", "88:F12",
                "199:Home", "200:↑", "201:Page Up", "203:←", "205:→", "207:End", "208:↓", "209:Page Down", "210:Insert", "211:Delete"
            };
            return rows.Select(row => row.Split(':')).Select(row => new OarChoice(row[0], row[1])).ToArray();
        }
    }
}
