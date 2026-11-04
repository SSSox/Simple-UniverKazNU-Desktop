using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ScriptContextsTests
    {
        private static void Check(ScriptContexts contexts, int id, bool trusted, bool main)
        {
            bool isTrusted, isMain; contexts.Check(id, out isTrusted, out isMain);
            Assert.Equal(trusted, isTrusted); Assert.Equal(main, isMain);
        }
        [Fact]
        public void OnlyUniversDefaultContextInTheMainFrameIsTheMainDocument()
        {
            var contexts = new ScriptContexts();
            contexts.MainFrame("main");
            contexts.Created(1, "https://univer.kaznu.kz", "main");
            contexts.Created(2, "https://univer.kaznu.kz", "child");
            contexts.Created(3, "https://univer.kaznu.kz", null);
            contexts.Created(4, "https://example.com", "main");
            Check(contexts, 1, true, true);
            Check(contexts, 2, true, false);
            Check(contexts, 3, true, false);
            Check(contexts, 4, false, true);
            Check(contexts, 9, false, false);
        }
        [Fact]
        public void NavigationAndDestroyedContextsLoseTheirStanding()
        {
            var contexts = new ScriptContexts();
            contexts.MainFrame("first");
            contexts.Created(1, "https://univer.kaznu.kz", "first");
            contexts.MainFrame("second");
            Check(contexts, 1, true, false);
            contexts.Created(2, "https://univer.kaznu.kz", "second");
            contexts.Destroyed(2);
            Check(contexts, 2, false, false);
            contexts.Created(3, "https://univer.kaznu.kz", "second");
            contexts.Cleared();
            Check(contexts, 3, false, false);
        }
    }
}
