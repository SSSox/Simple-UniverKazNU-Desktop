using System;
using System.Collections.Generic;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class FontChoiceTests
    {
        private static HashSet<string> Fonts(params string[] names) { return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase); }
        [Fact]
        public void Pick_SubstitutedSegoeUiDoesNotTurnTheInterfaceMonospace()
        {
            var choice = FontChoice.Pick(Fonts("Segoe UI", "Segoe UI Variable Text", "Segoe UI Variable Text Semibold"), name => name == "Segoe UI");
            Assert.Equal("Segoe UI Variable Text", choice.Regular);
            Assert.Equal("Segoe UI Variable Text Semibold", choice.Strong);
        }
        [Fact]
        public void Pick_ClassicWindowsKeepsSegoeUiWithRealBold()
        {
            var choice = FontChoice.Pick(Fonts("Segoe UI", "Segoe UI Semibold", "Tahoma"), name => false);
            Assert.Equal("Segoe UI", choice.Regular);
            Assert.Null(choice.Strong);
        }
        [Fact]
        public void Pick_SubstitutedSegoeUiWithoutVariableFontFallsBackToTahoma()
        { Assert.Equal("Tahoma", FontChoice.Pick(Fonts("Segoe UI", "Tahoma"), name => name == "Segoe UI").Regular); }
        [Fact]
        public void IsSubstituted_MatchesRegistryValueNamesExactlyOrByCharset()
        {
            Assert.True(FontChoice.IsSubstituted(new[] { "Helv", "Segoe UI", "MS Shell Dlg" }, "Segoe UI"));
            Assert.True(FontChoice.IsSubstituted(new[] { "segoe ui,204" }, "Segoe UI"));
            Assert.False(FontChoice.IsSubstituted(new[] { "Segoe UI" }, "Segoe UI Variable Text"));
        }
    }
}
