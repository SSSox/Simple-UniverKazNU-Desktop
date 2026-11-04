using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public sealed class SettingsDecisionTests
    {
        [Theory]
        [InlineData(false, "AppearanceOnly")]
        [InlineData(true, "ResumeSignIn")]
        public void SameAccountPreservesActiveSessionOrExplicitlyResumes(bool paused, string expected)
        { Assert.Equal(expected, SettingsDecision.Classify(Account("a", "b"), Account("a", "b"), paused).ToString()); }
        [Theory]
        [InlineData("A", "b")]
        [InlineData("a", "B")]
        [InlineData("other", "b")]
        public void AccountChangesRequireNewSession(string login, string password)
        { Assert.Equal(SettingsChange.NewCredentials, SettingsDecision.Classify(Account("a", "b"), Account(login, password), false)); }
        [Fact]
        public void AppearanceWithoutSavedAccountNeverStartsLogin()
        { Assert.Equal(SettingsChange.AppearanceOnly, SettingsDecision.Classify(null, null, true)); }
        [Fact]
        public void FirstAccountRequiresNewSession()
        { Assert.Equal(SettingsChange.NewCredentials, SettingsDecision.Classify(null, Account("a", "b"), false)); }
        private static Credentials Account(string login, string password) { return new Credentials { Login = login, Password = password }; }
    }
}
