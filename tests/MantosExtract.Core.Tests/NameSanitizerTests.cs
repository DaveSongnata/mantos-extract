using MantosExtract.Core;
using Xunit;

namespace MantosExtract.Core.Tests
{
    public class NameSanitizerTests
    {
        [Fact]
        public void Sanitize_RemovesFilesystemInvalidChars()
        {
            string result = NameSanitizer.Sanitize("logo: peixe / azul");

            Assert.DoesNotContain(":", result);
            Assert.DoesNotContain("/", result);
        }

        [Fact]
        public void Sanitize_CollapsesWhitespaceLeftBehindByRemovedChars()
        {
            string result = NameSanitizer.Sanitize("Cliente:   José");

            Assert.Equal("Cliente José", result);
        }

        [Fact]
        public void Sanitize_TrimsTrailingDotsAndSpaces_WindowsReservesThem()
        {
            string result = NameSanitizer.Sanitize("logo... ");

            Assert.Equal("logo", result);
        }

        [Fact]
        public void Sanitize_ReservedDeviceName_GetsDisambiguated()
        {
            string result = NameSanitizer.Sanitize("CON");

            Assert.NotEqual("CON", result);
            Assert.Contains("CON", result);
        }

        [Fact]
        public void Sanitize_EmptyOrWhitespace_FallsBackToDefault()
        {
            Assert.Equal("elemento", NameSanitizer.Sanitize(""));
            Assert.Equal("elemento", NameSanitizer.Sanitize("   "));
            Assert.Equal("elemento", NameSanitizer.Sanitize(null));
        }

        [Fact]
        public void Sanitize_EverythingStripped_FallsBackToDefault()
        {
            string result = NameSanitizer.Sanitize("::://///");

            Assert.Equal("elemento", result);
        }

        [Fact]
        public void Sanitize_VeryLongLabel_IsTruncated()
        {
            string longLabel = new string('a', 200);

            string result = NameSanitizer.Sanitize(longLabel);

            Assert.True(result.Length <= 80);
        }

        [Fact]
        public void Sanitize_PreservesAccentedPortugueseCharacters()
        {
            string result = NameSanitizer.Sanitize("José & Cia Confecções");

            Assert.Contains("José", result);
            Assert.Contains("Confecções", result);
        }

        [Fact]
        public void WithSuffix_DisambiguatesRepeatedLabels()
        {
            Assert.Equal("logo_1", NameSanitizer.WithSuffix("logo", 1));
            Assert.Equal("logo_2", NameSanitizer.WithSuffix("logo", 2));
        }
    }
}
