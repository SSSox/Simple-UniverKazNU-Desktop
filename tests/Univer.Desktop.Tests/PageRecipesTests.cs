using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // Loading placeholders of the simpler pages: each page the owner listed gets its own recipe, sub-pages are told apart
    // from their lists, and pages with a placeholder of their own (home, schedule, journal, УМКД) get none.
    public class PageRecipesTests
    {
        [Theory]
        [InlineData("/student/monrknews", nameof(RecipePart.News))]
        [InlineData("/my/faq", nameof(RecipePart.Accordion))]
        [InlineData("/my/docs", nameof(RecipePart.Search))]
        [InlineData("/my/messages/inbox", nameof(RecipePart.SelectCard))]
        [InlineData("/my/calculatorgpa", nameof(RecipePart.Inputs))]
        [InlineData("/my/appeals", nameof(RecipePart.Action))]
        [InlineData("/my/appeals/new", nameof(RecipePart.Form))]
        [InlineData("/student/rating/", nameof(RecipePart.Table))]
        [InlineData("/student/rating/57", nameof(RecipePart.Cards))]
        [InlineData("/student/rating/57/go", nameof(RecipePart.Questions))]
        [InlineData("/student/mydata", nameof(RecipePart.Photo))]
        [InlineData("/student/practik", nameof(RecipePart.DocRows))]
        [InlineData("/student/advicer", nameof(RecipePart.Facts))]
        [InlineData("/student/contract", nameof(RecipePart.Pager))]
        [InlineData("/student/academcalendar/", nameof(RecipePart.Tabs))]
        [InlineData("/student/myexam", nameof(RecipePart.DocCards))]
        [InlineData("/student/mymidterm", nameof(RecipePart.Empty))]
        [InlineData("/student/educplan", nameof(RecipePart.Facts))]
        [InlineData("/student/profile", nameof(RecipePart.Rows))]
        [InlineData("/student/stud/rating/anket/", nameof(RecipePart.Stats))]
        [InlineData("/student/stud/rating/anket/achievements/add", nameof(RecipePart.Fields))]
        [InlineData("/student/testlist", nameof(RecipePart.Video))]
        [InlineData("/student/fail", nameof(RecipePart.Boxed))]
        [InlineData("/student/failfx", nameof(RecipePart.Boxed))]
        [InlineData("/student/gpaincrease", nameof(RecipePart.Centred))]
        [InlineData("/student/attestation/", nameof(RecipePart.Column))]
        public void EachListedPageHasItsOwnRecipe(string path, string telling)
        {
            var recipe = PageRecipes.Find(path);
            Assert.NotNull(recipe);
            Assert.Contains(recipe, step => step.Part.ToString() == telling);
        }

        // The addresses the site's own menu opens (the owner's list of 18 Nov 2026) find the same recipes as the short forms above.
        [Theory]
        [InlineData("/my/messages/index/", nameof(RecipePart.SelectCard))]
        [InlineData("/my/appeals/index/", nameof(RecipePart.Action))]
        [InlineData("/student/mydata/edit/", nameof(RecipePart.Photo))]
        [InlineData("/student/practik/indexlist/", nameof(RecipePart.DocRows))]
        [InlineData("/student/contract/list/", nameof(RecipePart.Pager))]
        [InlineData("/student/myexam/schedule/", nameof(RecipePart.DocCards))]
        [InlineData("/student/mymidterm/schedule/", nameof(RecipePart.Empty))]
        [InlineData("/student/TestList", nameof(RecipePart.Video))]
        [InlineData("/student/rating", nameof(RecipePart.Table))]
        [InlineData("/student/stud/rating/anket", nameof(RecipePart.Stats))]
        public void TheSiteMenusOwnAddressesFindTheirRecipes(string path, string telling)
        {
            EachListedPageHasItsOwnRecipe(path, telling);
        }

        [Fact]
        public void AQuestionnaireIsNotItsListAndTheSecondFaqPageIsNotTheFirst()
        {
            Assert.Equal(RecipePart.Questions, PageRecipes.Find("/student/rating/57/go").Last().Part);
            Assert.Equal(RecipePart.Cards, PageRecipes.Find("/student/rating/57/").Last().Part);
            // The second FAQ page opens long answers: tall rows, unlike the first page's list of questions.
            Assert.Equal(1, PageRecipes.Find("/my/faq/2").Single(step => step.Part == RecipePart.Accordion).B);
            Assert.Equal(0, PageRecipes.Find("/my/FAQ").Single(step => step.Part == RecipePart.Accordion).B);
        }

        [Theory]
        [InlineData("/news/student/1/")]
        [InlineData("/student/myschedule/")]
        [InlineData("/student/attendance/full/")]
        [InlineData("/student/umkd/")]
        [InlineData("/student/bachelor/")]
        [InlineData("/student/ratingx")]
        [InlineData("/my/docsarchive")]
        [InlineData("")]
        [InlineData(null)]
        public void PagesWithTheirOwnPlaceholderOrUnknownOnesHaveNone(string path)
        {
            Assert.Null(PageRecipes.Find(path));
        }
    }
}
