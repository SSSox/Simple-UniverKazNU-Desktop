using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.Web
{
    internal static class SingleSignOn
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.SignOn");
        // A POST hand-off cannot be passed on a command line. A short-lived page in the user's own TEMP
        // folder posts it once and is deleted after the browser has read it.
        public static string WriteHandOffPage(string url, string method, IEnumerable<KeyValuePair<string, string>> fields, string title)
        {
            Directory.CreateDirectory(Folder);
            CleanUp(TimeSpan.FromMinutes(2)); // Pages of a hand-off that is still starting belong to their own 30-second cleanup.
            var file = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(file, SignOnPage.AutoSubmitPage(url, method, fields, title), new UTF8Encoding(false));
            Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => Delete(file));
            return file;
        }
        public static void CleanUp(TimeSpan age)
        {
            try
            {
                if (!Directory.Exists(Folder)) return;
                foreach (var file in Directory.GetFiles(Folder, "*.html"))
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= age) Delete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private static void Delete(string file) { try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        // Form fields of a navigation that leaves Univer; only urlencoded bodies can be replayed by the browser.
        public static List<KeyValuePair<string, string>> PostFields(IRequest request)
        {
            var type = request.GetHeaderByName("Content-Type") ?? "";
            var data = request.PostData;
            if (data == null || type.IndexOf("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) < 0) return null;
            var body = new StringBuilder();
            foreach (var element in data.Elements)
                if (element.Type == PostDataElementType.Bytes && element.Bytes != null) body.Append(Encoding.UTF8.GetString(element.Bytes));
            return SignOnPage.ParseForm(body.ToString());
        }
    }
}
