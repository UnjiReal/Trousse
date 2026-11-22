using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SkyrimOutfit
{
    internal sealed class OarSettings
    {
        private readonly byte[] original;
        private readonly Encoding encoding;
        private readonly bool withBom;
        private readonly string sourceText;
        internal readonly string Source;
        internal readonly List<Entry> Entries = new List<Entry>();

        internal OarSettings(string path, bool defaults = false)
        {
            Source = Path.GetFullPath(path);
            original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (original == null && !defaults) throw new FileNotFoundException(Path.GetFileName(path), path);
            byte[] input = original ?? Encoding.UTF8.GetBytes(DefaultText());
            using (var reader = new StreamReader(new MemoryStream(input), new UTF8Encoding(false), true))
            {
                sourceText = reader.ReadToEnd();
                encoding = reader.CurrentEncoding;
            }
            byte[] preamble = encoding.GetPreamble();
            withBom = preamble.Length > 0 && input.Take(preamble.Length).SequenceEqual(preamble);
            if (!withBom && sourceText.Contains("\uFFFD"))
            {
                encoding = Encoding.GetEncoding(1252);
                sourceText = encoding.GetString(input);
            }
            var linePattern = new Regex(
                @"^(?<comments>(?:[\t ]*[;#][^\r\n]*\r?\n)*)[\t ]*(?:\[(?<section>[^\]\r\n]+)\][^\r\n]*|(?<key>[^#;=\[\]\s][^=\r\n]*?)[\t ]*=[\t ]*(?<value>[^;\r\n]*?)[\t ]*(?:;(?<inline>[^\r\n]*))?)\r?$",
                RegexOptions.Multiline);
            string section = "";
            foreach (Match line in linePattern.Matches(sourceText))
            {
                if (line.Groups["section"].Success)
                {
                    section = line.Groups["section"].Value.Trim();
                    continue;
                }
                Group value = line.Groups["value"];
                var comments = line.Groups["comments"].Value.Split('\n').Select(comment => comment.Trim().TrimStart(';', '#').Trim())
                    .Concat(new[] { line.Groups["inline"].Value.Trim() }).Where(comment => comment.Length > 0);
                Entries.Add(new Entry(section, line.Groups["key"].Value.Trim(), value.Value, value.Index, value.Length,
                    String.Join(Environment.NewLine, comments)));
            }
            if (defaults)
                foreach (var option in OarOptions.Global.Select(row => row.Split('|')))
                    if (!Entries.Any(entry => Files.Comparer.Equals(entry.Section, option[0]) && Files.Comparer.Equals(entry.Key, option[1])))
                        Entries.Add(new Entry(option[0], option[1], option[2], -1, 0, ""));
        }

        internal void Save(string path)
        {
            if (Files.Comparer.Equals(Path.GetFullPath(path), Source)
                && (File.Exists(path) != (original != null) || original != null && !original.SequenceEqual(File.ReadAllBytes(path))))
                throw new IOException(Path.GetFileName(Source) + " changed on disk. Reload it before overwriting.");
            var text = new StringBuilder(sourceText);
            foreach (Entry entry in Entries.Where(entry => entry.IsChanged)) entry.Validate();
            foreach (Entry entry in Entries.Where(entry => entry.IsChanged && entry.Start >= 0).Reverse())
            {
                text.Remove(entry.Start, entry.Length);
                text.Insert(entry.Start, entry.Value);
            }
            string newLine = sourceText.Contains("\r\n") ? "\r\n" : "\n";
            foreach (var section in Entries.Where(entry => entry.IsChanged && entry.Start < 0).GroupBy(entry => entry.Section, Files.Comparer))
            {
                var headers = Regex.Matches(text.ToString(), @"^[\t ]*\[([^\]\r\n]+)\][^\r\n]*", RegexOptions.Multiline).Cast<Match>().ToArray();
                int index = Array.FindLastIndex(headers, header => Files.Comparer.Equals(header.Groups[1].Value.Trim(), section.Key));
                int position = index < 0 || index == headers.Length - 1 ? text.Length : headers[index + 1].Index;
                string prefix = position > 0 && text[position - 1] != '\n' ? newLine : "";
                if (index < 0) prefix += newLine + "[" + section.Key + "]" + newLine;
                text.Insert(position, prefix + String.Concat(section.Select(entry => entry.Key + " = " + entry.Value + newLine)));
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                {
                    if (withBom)
                    {
                        byte[] bom = encoding.GetPreamble();
                        stream.Write(bom, 0, bom.Length);
                    }
                    byte[] data = encoding.GetBytes(text.ToString());
                    stream.Write(data, 0, data.Length);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string DefaultText()
        {
            return String.Join(Environment.NewLine + Environment.NewLine, OarOptions.Global.Select(row => row.Split('|'))
                .GroupBy(row => row[0]).Select(group => "[" + group.Key + "]" + Environment.NewLine +
                    String.Join(Environment.NewLine, group.Select(row => row[1] + " = " + row[2])))) + Environment.NewLine;
        }

        internal sealed class Entry
        {
            public string Key { get; private set; }
            public string Value { get; set; }
            internal readonly string Section;
            internal readonly string Comment;
            internal readonly int Start, Length;
            private readonly string original;

            internal Entry(string section, string key, string value, int start, int length, string comment)
            {
                Section = section;
                Key = key;
                Value = original = value;
                Start = start;
                Length = length;
                Comment = comment;
            }

            internal bool IsChanged { get { return original != Value; } }

            private static bool IsNumber(string value)
            {
                double result;
                return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                    && !Double.IsNaN(result) && !Double.IsInfinity(result);
            }

            internal void Validate()
            {
                bool flag;
                uint integer;
                if (Value == null || Value.IndexOfAny(new[] { '\r', '\n', ';', '\0' }) >= 0)
                    throw new InvalidDataException(Key + ": invalid value.");
                if (Boolean.TryParse(original, out flag) && !Boolean.TryParse(Value, out flag))
                    throw new InvalidDataException(Key + ": true / false.");
                if (Key.StartsWith("u", StringComparison.Ordinal) &&
                    UInt32.TryParse(original, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                {
                    if (!UInt32.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                        throw new InvalidDataException(Key + ": enter a non-negative integer.");
                }
                else if (IsNumber(original) && !IsNumber(Value))
                {
                    string normalized = Value.Replace(',', '.');
                    if (!IsNumber(normalized)) throw new InvalidDataException(Key + ": enter a number.");
                    Value = normalized;
                }
            }
        }
    }
}
