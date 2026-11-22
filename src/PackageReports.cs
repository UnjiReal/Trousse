using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SkyrimOutfit
{
    internal static class PackageReports
    {
        private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
        private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        private static readonly XNamespace Format = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
        private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
        internal static void Write(string package, Catalog catalog, Pack pack, bool includeModesty, bool italian,
            Dictionary<Recipe, List<Piece>> components, bool usesSmp)
        {
            var rows = new List<string[]> { new[] { "Vanilla equipment", "Game records", "Mesh", "Mashup",
                "Source mod", "Component" } };
            foreach (var outfit in pack.Outfits)
            {
                Target target = catalog.ByTarget[outfit.TargetKey];
                string records = String.Join(", ", Items(target, outfit).Select(item => item.ConsoleId));
                foreach (var piece in components[outfit])
                {
                    rows.Add(new[] { target.DisplayName(false), records, VariantPatch.Output(outfit), outfit.Name, piece.ModName, piece.Name });
                }
            }
            var rules = includeModesty ? ModestyRules.Resolve(catalog, pack.Outfits) : new ModestyRule[0];
            if (rules.Length > 0)
            {
                ModestyRules.WriteKid(Path.Combine(package, ModestyRules.KidName(pack.Name)), rules);
                RacialModesty.Write(catalog.DataRoot, package, rules);
            }
            if (includeModesty)
                new PackageDependencies(catalog, components.Values.SelectMany(pieces => pieces), usesSmp, rules).Write(package, pack.Name, italian);
            WriteSpreadsheet(Path.Combine(package, "outfit-list.ods"), rows);
            WriteOutfitDocument(Path.Combine(package, "outfit-list.odt"), catalog, pack, italian, components);
        }

        internal static string NexusUrl(string id) { return "https://www.nexusmods.com/skyrimspecialedition/mods/" + id; }

        private static IEnumerable<GameItem> Items(Target target, Recipe outfit)
        {
            return target.Items.Where(item => outfit.VariantIds == null || outfit.VariantIds.Count == 0 ||
                outfit.VariantIds.Contains(item.RuleId, Files.Comparer));
        }

        internal static void WriteOutfitDocument(string path, Catalog catalog, Pack pack, bool italian,
            Dictionary<Recipe, List<Piece>> components)
        {
            var body = new XElement(Office + "text", Paragraph("ReportTitle", pack.Name),
                Paragraph("ReportSubtitle", italian ? "Vestiti di Skyrim e mod sorgenti" : "Skyrim outfits and source mods"));
            foreach (var recipe in pack.Outfits)
            {
                var target = catalog.ByTarget[recipe.TargetKey];
                body.Add(new XElement(TextNs + "h", new XAttribute(TextNs + "outline-level", "1"),
                    new XAttribute(TextNs + "style-name", "OutfitHeading"), target.DisplayName(italian)));
                body.Add(Paragraph("OutfitInfo", recipe.Name + " · " + App.EquipmentView(target.ViewKind, italian)));
                string ids = String.Join(", ", Items(target, recipe).Select(item => item.ConsoleId).Distinct().OrderBy(id => id));
                if (ids.Length != 0) body.Add(Paragraph("OutfitInfo", (italian ? "ID console " : "Console IDs ") + ids));
                foreach (var mod in components[recipe].GroupBy(piece => new { piece.ModName, piece.NexusId }))
                {
                    object name = mod.Key.NexusId == null ? (object)mod.Key.ModName : new XElement(TextNs + "a",
                        new XAttribute(XLink + "type", "simple"), new XAttribute(XLink + "href", NexusUrl(mod.Key.NexusId)), mod.Key.ModName);
                    body.Add(Paragraph("ModHeading", name));
                    foreach (string piece in mod.Select(part => part.Name).Distinct(Files.Comparer))
                        body.Add(Paragraph("PieceName", piece));
                }
            }
            var styles = new XElement(Office + "automatic-styles",
                ParagraphStyle("ReportTitle", "24pt", "#24272B", "0cm", "0.15cm", false),
                ParagraphStyle("ReportSubtitle", "11pt", "#4C5359", "0cm", "0.5cm", false),
                ParagraphStyle("OutfitHeading", "16pt", "#24272B", "0cm", "0.14cm", true),
                ParagraphStyle("OutfitInfo", "9pt", "#53595F", "0cm", "0.14cm", true),
                ParagraphStyle("ModHeading", "11pt", "#24272B", "0.35cm", "0.04cm", true),
                ParagraphStyle("PieceName", "10pt", "#24272B", "0.7cm", "0.1cm", false));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteOdf(temporary, "text", body, styles);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static XElement Paragraph(string style, object content)
        {
            return new XElement(TextNs + "p", new XAttribute(TextNs + "style-name", style), content);
        }

        private static XElement ParagraphStyle(string name, string size, string color, string left, string bottom, bool keep)
        {
            return new XElement(Style + "style", new XAttribute(Style + "name", name), new XAttribute(Style + "family", "paragraph"),
                new XElement(Style + "paragraph-properties", new XAttribute(Format + "margin-left", left),
                    new XAttribute(Format + "margin-top", name == "OutfitHeading" ? "0.55cm" : "0cm"),
                    new XAttribute(Format + "margin-bottom", bottom),
                    new XAttribute(Format + "keep-with-next", keep ? "always" : "auto")),
                new XElement(Style + "text-properties", new XAttribute(Format + "font-size", size), new XAttribute(Format + "color", color),
                    new XAttribute(Format + "font-weight", name.EndsWith("Heading", StringComparison.Ordinal) || name == "ReportTitle" ? "bold" : "normal")));
        }

        private static void WriteSpreadsheet(string path, List<string[]> rows)
        {
            var table = new XElement(Table + "table", new XAttribute(Table + "name", "Outfits"),
                new XElement(Table + "table-column", new XAttribute(Table + "number-columns-repeated", rows[0].Length)),
                rows.Select(row => new XElement(Table + "table-row", row.Select(value => new XElement(Table + "table-cell",
                    new XAttribute(Office + "value-type", "string"), new XElement(TextNs + "p", value))))));
            WriteOdf(path, "spreadsheet", new XElement(Office + "spreadsheet", table), null);
        }

        private static void WriteOdf(string path, string kind, XElement body, XElement styles)
        {
            string media = "application/vnd.oasis.opendocument." + kind;
            var content = new XElement(Office + "document-content",
                new XAttribute(XNamespace.Xmlns + "office", Office), new XAttribute(XNamespace.Xmlns + "table", Table),
                new XAttribute(XNamespace.Xmlns + "text", TextNs), new XAttribute(Office + "version", "1.2"),
                new XAttribute(XNamespace.Xmlns + "style", Style), new XAttribute(XNamespace.Xmlns + "fo", Format),
                new XAttribute(XNamespace.Xmlns + "xlink", XLink), styles, new XElement(Office + "body", body));
            var manifest = new XElement(Manifest + "manifest",
                new XAttribute(XNamespace.Xmlns + "manifest", Manifest), new XAttribute(Manifest + "version", "1.2"),
                ManifestEntry("/", media, true), ManifestEntry("content.xml", "text/xml", false),
                ManifestEntry("styles.xml", "text/xml", false));
            var documentStyles = new XElement(Office + "document-styles", new XAttribute(Office + "version", "1.2"),
                new XElement(Office + "styles"));
            OdfArchive.Write(path, new[] {
                new OdfArchive.Entry("mimetype", Encoding.ASCII.GetBytes(media)),
                new OdfArchive.Entry("content.xml", XmlBytes(content)),
                new OdfArchive.Entry("styles.xml", XmlBytes(documentStyles)),
                new OdfArchive.Entry("META-INF/manifest.xml", XmlBytes(manifest))
            });
        }

        private static XElement ManifestEntry(string path, string type, bool root)
        {
            return new XElement(Manifest + "file-entry", new XAttribute(Manifest + "full-path", path),
                new XAttribute(Manifest + "media-type", type), root ? new XAttribute(Manifest + "version", "1.2") : null);
        }

        private static byte[] XmlBytes(XElement root)
        {
            using (var stream = new MemoryStream())
            {
                new XDocument(root).Save(stream);
                return stream.ToArray();
            }
        }
    }
}
