using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class MaterialLinkGestureTests
    {
        private const string Material = SitePolicy.Origin + "/student/umkd/get/101/202/2026/1";
        private const string TeacherLink = "https://teacher.example.org/lecture";

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void ExternalMaterialRedirectRequiresTheOriginalDownloadGesture(bool userGesture, bool expected)
        {
            Assert.True(SitePolicy.MaterialLink(Material, TeacherLink));
            Assert.Equal(expected, SitePolicy.OpenMaterialLink(Material, TeacherLink, userGesture));
        }

        [Theory]
        [InlineData("https://univer.kaznu.kz/Content/lecture.pdf", TeacherLink)]
        [InlineData(Material, "mailto:teacher@example.org")]
        [InlineData(Material, "file:///C:/lecture.pdf")]
        [InlineData(Material, "http://127.0.0.1/admin")]
        [InlineData(Material, "https://univer.kaznu.kz/user/logout")]
        [InlineData(Material, null)]
        public void GestureDoesNotAuthorizeAnInvalidMaterialOrDestination(string material, string target)
        {
            Assert.False(SitePolicy.OpenMaterialLink(material, target, true));
        }
    }
}
