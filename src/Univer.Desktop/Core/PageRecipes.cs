using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    // Loading placeholders for the site's simpler pages, drawn from parts: each part is a block the site repeats from page to
    // page (a heading, an accordion list, a form card, a facts grid, a table, an empty state), in the order and number the
    // page shows them. LoadingView draws them; pages with a layout of their own (home, schedule, journal, УМКД) have none.
    internal enum RecipePart { Title, Subtitle, Back, Notice, Accordion, Search, Pills, Tabs, SelectCard, Form, Facts, Table, Empty, Pager, DocCards, DocRows, Stats, News, Action, Inputs, Video, ButtonNote, Footer, Photo, YearBar, Segment, Fields, Cards, Questions, Centred, Boxed, Rows, Column }
    internal struct RecipeStep { public RecipePart Part; public int A, B, C; }
    internal static class PageRecipes
    {
        private static RecipeStep S(RecipePart part, int a = 0, int b = 0, int c = 0) { return new RecipeStep { Part = part, A = a, B = b, C = c }; }
        private static KeyValuePair<Regex, RecipeStep[]> R(string path, params RecipeStep[] steps) { return new KeyValuePair<Regex, RecipeStep[]>(new Regex(path, RegexOptions.CultureInvariant), steps); }
        // Taken from the site's pages in the reading layout (November 2026); the first match wins. A: count or width,
        // B: kind (Accordion 1 tall, 2 with counts; Notice 1 warning, 2 card with a button; Facts pairs per row; Table rows),
        // C: Table's last column is a button.
        private static readonly KeyValuePair<Regex, RecipeStep[]>[] Recipes =
        {
            R(@"^/student/monrknews(/\d+)?/?$", S(RecipePart.Title, 150), S(RecipePart.News, 4)),
            R(@"^/my/faq/\d+/?$", S(RecipePart.Title, 260), S(RecipePart.Accordion, 8, 1)),
            R(@"^/my/faq/?$", S(RecipePart.Title, 260), S(RecipePart.Accordion, 12)),
            R(@"^/my/docs/?$", S(RecipePart.Back), S(RecipePart.Search), S(RecipePart.Accordion, 10, 2)),
            R(@"^/my/messages(/.*)?$", S(RecipePart.Title, 120), S(RecipePart.Pills, 2), S(RecipePart.SelectCard), S(RecipePart.Empty), S(RecipePart.Subtitle, 280), S(RecipePart.SelectCard)),
            R(@"^/my/calculatorgpa/?$", S(RecipePart.Title, 150), S(RecipePart.Inputs, 5)),
            R(@"^/my/appeals/new/?$", S(RecipePart.Form)),
            R(@"^/my/appeals(/.*)?$", S(RecipePart.Title, 220), S(RecipePart.Action), S(RecipePart.Empty), S(RecipePart.Footer)),
            R(@"^/student/rating/?$", S(RecipePart.Title, 420), S(RecipePart.Notice, 9), S(RecipePart.Table, 4, 1, 1), S(RecipePart.Footer)),
            R(@"^/student/rating/\d+/go/?$", S(RecipePart.Title, 360), S(RecipePart.Questions, 4)),
            R(@"^/student/rating/\d+/?$", S(RecipePart.Title, 480), S(RecipePart.Cards, 4)),
            R(@"^/student/mydata(/.*)?$", S(RecipePart.Title, 190), S(RecipePart.Notice, 0), S(RecipePart.Photo)),
            R(@"^/student/practik(/.*)?$", S(RecipePart.Title, 80), S(RecipePart.DocRows, 1, 1), S(RecipePart.Subtitle, 90, 1), S(RecipePart.Pager), S(RecipePart.Subtitle, 200), S(RecipePart.Table, 6, 0)),
            R(@"^/student/advicer/?$", S(RecipePart.Title, 200), S(RecipePart.Notice, 3), S(RecipePart.Subtitle, 110), S(RecipePart.Facts, 2, 2), S(RecipePart.Footer)),
            R(@"^/student/contract(/.*)?$", S(RecipePart.Title, 100), S(RecipePart.Subtitle, 190), S(RecipePart.Empty), S(RecipePart.Pager), S(RecipePart.Empty), S(RecipePart.Footer)),
            R(@"^/student/academcalendar(/.*)?$", S(RecipePart.Title, 240), S(RecipePart.Tabs, 8), S(RecipePart.Facts, 3, 2), S(RecipePart.Subtitle, 260), S(RecipePart.Table, 4, 6)),
            R(@"^/student/myexam(/.*)?$", S(RecipePart.Title, 120), S(RecipePart.DocCards, 5), S(RecipePart.Notice, 1, 1), S(RecipePart.Notice, 1, 1), S(RecipePart.Title, 200), S(RecipePart.Pager), S(RecipePart.Table, 6, 4)),
            R(@"^/student/mymidterm(/.*)?$", S(RecipePart.Title, 200), S(RecipePart.Empty), S(RecipePart.Footer)),
            R(@"^/student/educplan(/.*)?$", S(RecipePart.Title, 140), S(RecipePart.Facts, 3, 2), S(RecipePart.Facts, 11, 3)),
            R(@"^/student/profile/?$", S(RecipePart.Back), S(RecipePart.Title, 220), S(RecipePart.Subtitle, 90), S(RecipePart.Rows, 14)),
            R(@"^/student/stud/rating/anket/?$", S(RecipePart.YearBar), S(RecipePart.Pills, 3), S(RecipePart.Segment), S(RecipePart.Stats, 4), S(RecipePart.Footer)),
            R(@"^/student/stud/rating/anket/.+$", S(RecipePart.YearBar), S(RecipePart.Pills, 3), S(RecipePart.Subtitle, 200), S(RecipePart.Fields, 4)),
            R(@"^/student/testlist/?$", S(RecipePart.Title, 140), S(RecipePart.DocRows, 3), S(RecipePart.Video), S(RecipePart.ButtonNote), S(RecipePart.Title, 300), S(RecipePart.Pager), S(RecipePart.Empty)),
            R(@"^/student/fail/?$", S(RecipePart.Title, 130), S(RecipePart.Notice, 5, 2), S(RecipePart.Title, 260), S(RecipePart.Empty), S(RecipePart.Boxed), S(RecipePart.Footer)),
            R(@"^/student/failfx/?$", S(RecipePart.Title, 280), S(RecipePart.Empty), S(RecipePart.Boxed), S(RecipePart.Footer)),
            R(@"^/student/gpaincrease/?$", S(RecipePart.Title, 170), S(RecipePart.Centred), S(RecipePart.Empty), S(RecipePart.Boxed), S(RecipePart.Footer)),
            R(@"^/student/attestation(/.*)?$", S(RecipePart.Column, 960), S(RecipePart.Title, 240), S(RecipePart.Pills, 3), S(RecipePart.Table, 3, 9)),
        };
        // The recipe for a page path (case does not matter), or null for a page without one.
        public static RecipeStep[] Find(string path)
        {
            path = (path ?? "").ToLowerInvariant();
            foreach (var entry in Recipes) if (entry.Key.IsMatch(path)) return entry.Value;
            return null;
        }
    }
}
