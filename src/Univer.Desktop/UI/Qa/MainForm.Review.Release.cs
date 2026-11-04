#if !QA_REVIEW
using System.Threading.Tasks;

namespace Univer.Desktop.UI
{
    // Release-build stubs: the live-cabinet reviewer walks real signed-in pages, so it is compiled only into a QA build
    // (-p:QaReview=true) and a released executable cannot be pointed at the owner's session.
    internal sealed partial class MainForm
    {
        private void ReviewNavigated(string url, string method) { }
        private Task ReviewAccountAsync() { return Task.FromResult(true); }
    }
}
#endif
