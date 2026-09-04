using System;
using System.IO;
using System.Text.RegularExpressions;
using MantosExtract.Core.I18n;
using Xunit;

namespace MantosExtract.Core.Tests.I18n
{
    /// <summary>
    /// Cheap, C#-only guard against a typo'd <c>data-i18n</c> key in the docker's HTML — every
    /// key the page references must exist in the catalog, or it renders as the raw key text
    /// (LocalizedStrings.Get's documented fallback) instead of a translated label.
    ///
    /// This is NOT a substitute for a real JS syntax/undefined-function gate (Optimus's O18
    /// lesson: "not one [C# test] can see JavaScript" — a missing helper function broke every
    /// render silently for weeks despite 414 green tests). That gate is
    /// scripts/check-ui-js.js, planned for Fase 5 (plans/Phase_5.md); this test only catches
    /// key-name drift, cheaply, today.
    /// </summary>
    public class IndexHtmlKeysTests
    {
        [Fact]
        public void EveryDataI18nKeyInTheHtml_ExistsInTheCatalog()
        {
            string html = File.ReadAllText(FindIndexHtml());
            var pattern = new Regex("data-i18n(?:-title|-placeholder)?=\"([^\"]+)\"");

            foreach (Match match in pattern.Matches(html))
            {
                string key = match.Groups[1].Value;
                Assert.True(LocalizedStrings.HasExplicit(Language.Pt, key),
                    $"index.html referencia a chave '{key}', ausente do catálogo pt-BR");
            }
        }

        /// <summary>Walks up from the test's output directory looking for the repo-relative
        /// HTML path — resilient to the exact bin/Debug/net8.0 nesting changing.</summary>
        private static string FindIndexHtml()
        {
            const string relative = "src/MantosExtract.AddIn/wwwroot/index.html";
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);

            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException($"Não achei '{relative}' subindo a partir de {AppContext.BaseDirectory}");
        }
    }
}
