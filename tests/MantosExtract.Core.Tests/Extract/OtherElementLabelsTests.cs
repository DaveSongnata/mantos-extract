using MantosExtract.Core.Detect;
using MantosExtract.Core.Extract;
using Xunit;

namespace MantosExtract.Core.Tests.Extract
{
    /// <summary>Camisa "Sou Patriota" (Davidson, 2026-10-13): cada extração leva os rótulos dos
    /// OUTROS elementos detectados, pra a IA deixar de fora o vizinho que já é uma peça própria.</summary>
    public class OtherElementLabelsTests
    {
        private static DetectedElement El(string id, string label) => new DetectedElement(id, label, new BoundingBox(0, 0, 10, 10));

        [Fact]
        public void ListsEveryOtherDetectedElement_InDetectionOrder()
        {
            var all = new[] { El("el_1", "texto Sou"), El("el_2", "texto PATRIOTA"), El("el_3", "bandeira do Brasil frontal") };

            Assert.Equal(new[] { "texto Sou", "texto PATRIOTA" }, OtherElementLabels.For(all, all[2]));
        }

        [Fact]
        public void IncludesElementsTheOperatorDidNotSelect()
        {
            // A lista é da DETECÇÃO, não da seleção: o texto é peça própria mesmo que o operador não
            // o extraia agora.
            var all = new[] { El("el_1", "texto Sou"), El("el_3", "bandeira") };

            Assert.Contains("texto Sou", OtherElementLabels.For(all, all[1]));
        }

        [Fact]
        public void DropsItsOwnLabel_AndDuplicates()
        {
            var all = new[]
            {
                El("el_4", "bandeira na manga"), El("el_5", "Bandeira na manga "),
                El("el_9", "texto DEUS"), El("el_10", "texto deus"), El("el_11", "  "),
            };

            Assert.Equal(new[] { "texto DEUS" }, OtherElementLabels.For(all, all[0]));
        }

        [Fact]
        public void NoOtherElements_IsAnEmptyList()
        {
            var all = new[] { El("el_1", "girassol") };

            Assert.Empty(OtherElementLabels.For(all, all[0]));
        }
    }
}
