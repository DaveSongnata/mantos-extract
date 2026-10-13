using System.Linq;
using System.Text.Json;
using MantosExtract.Core.Detect;
using MantosExtract.Core.Extract;
using Xunit;

namespace MantosExtract.Core.Tests.Extract
{
    /// <summary>Camisa "Sou Patriota" (Davidson, 2026-10-13): cada extração leva os OUTROS elementos
    /// detectados (rótulo + caixa), pra a IA deixar de fora o vizinho que já é uma peça própria.</summary>
    public class OtherElementsTests
    {
        private static DetectedElement El(string id, string label, int x0 = 0, int y0 = 0, int x1 = 10, int y1 = 10) =>
            new DetectedElement(id, label, new BoundingBox(x0, y0, x1, y1));

        private static string[] Labels(DetectedElement[] all, int current) =>
            OtherElements.For(all, all[current]).Select(e => e.Label).ToArray();

        [Fact]
        public void ListsEveryOtherDetectedElement_InDetectionOrder()
        {
            var all = new[] { El("el_1", "texto Sou"), El("el_2", "texto PATRIOTA"), El("el_3", "bandeira do Brasil frontal") };

            Assert.Equal(new[] { "texto Sou", "texto PATRIOTA" }, Labels(all, 2));
        }

        [Fact]
        public void IncludesElementsTheOperatorDidNotSelect()
        {
            // A lista é da DETECÇÃO, não da seleção: o texto é peça própria mesmo que o operador não
            // o extraia agora.
            var all = new[] { El("el_1", "texto Sou"), El("el_3", "bandeira") };

            Assert.Contains("texto Sou", Labels(all, 1));
        }

        [Fact]
        public void DropsItsOwnLabel_AndDuplicates()
        {
            var all = new[]
            {
                El("el_4", "bandeira na manga"), El("el_5", "Bandeira na manga "),
                El("el_9", "texto DEUS"), El("el_10", "texto deus"), El("el_11", "  "),
            };

            Assert.Equal(new[] { "texto DEUS" }, Labels(all, 0));
        }

        [Fact]
        public void NoOtherElements_IsAnEmptyList()
        {
            var all = new[] { El("el_1", "girassol") };

            Assert.Empty(OtherElements.For(all, all[0]));
        }

        [Fact]
        public void ToJson_CarriesLabelAndBox_InTheServerFieldNames()
        {
            string json = OtherElements.ToJson(new[] { El("el_12", " texto ACIMA DE TODOS ", 622, 784, 874, 850) });

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement item = doc.RootElement[0];
            Assert.Equal("texto ACIMA DE TODOS", item.GetProperty("label").GetString());
            Assert.Equal(622, item.GetProperty("x_min").GetInt32());
            Assert.Equal(784, item.GetProperty("y_min").GetInt32());
            Assert.Equal(874, item.GetProperty("x_max").GetInt32());
            Assert.Equal(850, item.GetProperty("y_max").GetInt32());
        }
    }
}
