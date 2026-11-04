using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Univer.Desktop.Core
{
    // Parts of an Office Open XML package (XLSX, PPTX): relationships with their targets resolved to entry names, and
    // pictures read within the preview's budget.
    internal static class OfficePackage
    {
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        internal sealed class Relation { public string Type, Target; public bool External; }
        public static Dictionary<string, Relation> Relations(ZipArchive archive, string part, CancellationToken cancellation)
        {
            var result = new Dictionary<string, Relation>(StringComparer.Ordinal);
            var folder = part.Contains("/") ? part.Substring(0, part.LastIndexOf('/')) : "";
            var entry = archive.GetEntry((folder.Length > 0 ? folder + "/" : "") + "_rels/" + part.Substring(part.LastIndexOf('/') + 1) + ".rels");
            if (entry == null) return result;
            XDocument document;
            try { document = DocxHtml.ReadXml(entry, 4L * 1024 * 1024, cancellation); }
            catch (XmlException) { return result; }
            foreach (var relation in document.Root?.Elements(Rel + "Relationship") ?? Enumerable.Empty<XElement>())
            {
                var id = (string)relation.Attribute("Id"); var target = (string)relation.Attribute("Target");
                if (id == null || target == null) continue;
                var external = (string)relation.Attribute("TargetMode") == "External";
                result[id] = new Relation { Type = (string)relation.Attribute("Type") ?? "", Target = external ? target : Resolve(folder, target), External = external };
            }
            return result;
        }
        // "../media/image1.png" from folder "ppt/slides" is "ppt/media/image1.png"; "/xl/worksheets/sheet1.xml" is absolute.
        public static string Resolve(string folder, string target)
        {
            var parts = new List<string>();
            if (!target.StartsWith("/", StringComparison.Ordinal) && folder.Length > 0) parts.AddRange(folder.Split('/'));
            foreach (var piece in Uri.UnescapeDataString(target).Split('/'))
            {
                if (piece.Length == 0 || piece == ".") continue;
                if (piece == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(piece);
            }
            return string.Join("/", parts);
        }
        public static string ImageType(string name)
        {
            switch (Path.GetExtension(name ?? "").ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                default: return null;
            }
        }
        // Adds a picture part. Returns false for parts this viewer does not show (missing or another format); path is
        // null when the picture is larger than the viewer takes.
        public static bool Image(ZipArchive archive, string name, PreviewImages images, CancellationToken cancellation, out string path)
        {
            path = null;
            var entry = name == null ? null : archive.GetEntry(name);
            var mime = entry == null ? null : ImageType(entry.Name);
            if (mime == null) return false;
            path = images.Find(entry.FullName);
            if (path != null || !images.Fits(entry.Length)) return true;
            cancellation.ThrowIfCancellationRequested();
            // The header's size can understate the data: .NET Framework inflates entries without checking it.
            using (var stream = entry.Open()) using (var memory = new MemoryStream())
            {
                var buffer = new byte[81920]; int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!images.Fits(memory.Length + read)) return true;
                    memory.Write(buffer, 0, read);
                }
                path = images.Add(entry.FullName, memory.ToArray(), mime);
                return true;
            }
        }
    }
}
