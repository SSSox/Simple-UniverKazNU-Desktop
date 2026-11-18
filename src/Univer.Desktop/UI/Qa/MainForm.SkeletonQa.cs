using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // Loading placeholders of the simpler pages (Core/PageRecipes.cs) and of the link window, drawn at the window's width,
    // in a narrow window and in the dark theme: each page gets a picture of its own, and none is blank.
    internal sealed partial class MainForm
    {
        private static readonly string[] SkeletonQaRoutes =
        {
            "/student/monrknews", "/my/faq", "/my/faq/2", "/my/docs", "/my/messages", "/my/calculatorgpa", "/my/appeals", "/my/appeals/new",
            "/student/rating/", "/student/rating/57", "/student/rating/57/go", "/student/mydata", "/student/practik", "/student/advicer",
            "/student/contract", "/student/academcalendar", "/student/myexam", "/student/mymidterm", "/student/educplan", "/student/profile",
            "/student/stud/rating/anket/", "/student/stud/rating/anket/achievements/add", "/student/testlist", "/student/fail",
            "/student/failfx", "/student/gpaincrease", "/student/attestation", "/user/login"
        };
        private async Task SkeletonQaAsync(string qa, List<string> results)
        {
            var captured = loading.Captured; loading.Captured = null;
            var size = Size; var state = WindowState; var themeBefore = prefs.Theme;
            try
            {
                foreach (var width in new[] { 0, 760 })
                {
                    if (width > 0) { WindowState = System.Windows.Forms.FormWindowState.Normal; Size = new Size(Math.Max(MinimumSize.Width, width), 820); await Task.Delay(600); }
                    var blank = new List<string>(); var pictures = new Dictionary<string, string>(); var same = new List<string>();
                    foreach (var route in SkeletonQaRoutes)
                    {
                        var name = "skeleton-" + (width > 0 ? "narrow-" : "") + route.Trim('/').Replace('/', '-');
                        var ink = await SkeletonQaShotAsync(qa, SitePolicy.Origin + route, name);
                        if (ink.Item1 < .03) blank.Add(route + " " + ink.Item1.ToString("0.000"));
                        string other; if (pictures.TryGetValue(ink.Item2, out other)) same.Add(other + " = " + route); else pictures[ink.Item2] = route;
                    }
                    var where = width > 0 ? "in a narrow window" : "at the window's width";
                    Check(blank.Count == 0, "every listed page has a placeholder " + where + (blank.Count > 0 ? " (blank: " + string.Join(", ", blank) + ")" : ""), results);
                    Check(same.Count == 0, "no two listed pages share one placeholder " + where + (same.Count > 0 ? " (" + string.Join(", ", same) + ")" : ""), results);
                    var web = await SkeletonQaShotAsync(qa, "web", "skeleton-" + (width > 0 ? "narrow-" : "") + "web");
                    Check(web.Item1 >= .02, "the link window's cover shows a column of text " + where, results);
                }
                // 200 % in the narrow window: pills wrap, IGPA's switch goes under its heading; nothing is blank.
                var zoomed = new List<string>();
                foreach (var route in new[] { "/student/stud/rating/anket/", "/student/stud/rating/anket/achievements/add", "/my/messages", "/student/attestation", "/user/login" })
                {
                    var ink = await SkeletonQaShotAsync(qa, SitePolicy.Origin + route, "skeleton-zoom-" + route.Trim('/').Replace('/', '-'), 2);
                    if (ink.Item1 < .03) zoomed.Add(route);
                }
                Check(zoomed.Count == 0, "placeholders hold at 200 % in a narrow window" + (zoomed.Count > 0 ? " (" + string.Join(", ", zoomed) + ")" : ""), results);
                Size = size; WindowState = state; await Task.Delay(600);
                prefs.Theme = ThemeChoice.Dark; ApplyTheme();
                var dark = new List<string>();
                foreach (var route in new[] { "/student/rating/57", "/student/stud/rating/anket/", "/student/myexam", "/my/calculatorgpa", "/user/login" })
                {
                    var ink = await SkeletonQaShotAsync(qa, SitePolicy.Origin + route, "skeleton-dark-" + route.Trim('/').Replace('/', '-'));
                    if (ink.Item1 < .03 || ink.Item3 != Theme.DarkPalette.Background.ToArgb()) dark.Add(route + " " + ink.Item1.ToString("0.000"));
                }
                Check(dark.Count == 0, "the placeholders take the dark theme" + (dark.Count > 0 ? " (" + string.Join(", ", dark) + ")" : ""), results);
            }
            finally
            {
                loading.Visible = false; loading.Captured = captured; loading.Zoom = zoomPercent / 100f;
                prefs.Theme = themeBefore; ApplyTheme(false);
                Size = size; WindowState = state; await Task.Delay(300);
            }
        }
        // Draws the placeholder for a route and saves it; returns the share of pixels that are not the page's background, a
        // fingerprint of the picture, and the page colour it was drawn on.
        private async Task<Tuple<double, string, int>> SkeletonQaShotAsync(string qa, string route, string name, float zoom = 1)
        {
            loading.Zoom = zoom; loading.SetRoute(route); loading.Visible = true; loading.BringToFront(); loading.Refresh();
            await Task.Delay(30);
            var path = Path.Combine(qa, name + ".png"); SaveBitmap(loading, path);
            using (var shot = new Bitmap(path))
            {
                var background = loading.BackColor.ToArgb(); long inked = 0, all = 0, print = 17;
                for (var y = 0; y < shot.Height; y += 3)
                    for (var x = 0; x < shot.Width; x += 3)
                    {
                        var pixel = shot.GetPixel(x, y).ToArgb(); all++;
                        if (pixel != background) { inked++; print = unchecked(print * 31 + x * 7919 + y); }
                    }
                return Tuple.Create(all == 0 ? 0 : (double)inked / all, inked + ":" + print, background);
            }
        }
    }
}
