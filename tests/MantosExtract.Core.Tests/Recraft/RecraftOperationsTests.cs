using MantosExtract.Core.Recraft;
using Xunit;

namespace MantosExtract.Core.Tests.Recraft
{
    public class RecraftOperationsTests
    {
        [Fact]
        public void EndpointPath_PointsAtTheDedicatedMantosfcRoutes()
        {
            Assert.Equal("/api/v1/mantos-extract/remove-background", RecraftOperations.EndpointPath(RecraftOperation.RemoveBackground));
            Assert.Equal("/api/v1/mantos-extract/vectorize", RecraftOperations.EndpointPath(RecraftOperation.Vectorize));
        }

        [Fact]
        public void ProducesVector_OnlyForVectorize()
        {
            Assert.True(RecraftOperations.ProducesVector(RecraftOperation.Vectorize));
            Assert.False(RecraftOperations.ProducesVector(RecraftOperation.RemoveBackground));
        }

        [Fact]
        public void NameSuffix_DistinguishesTheNewShapeFromTheOriginal()
        {
            Assert.Equal(" (sem fundo)", RecraftOperations.NameSuffix(RecraftOperation.RemoveBackground));
            Assert.Equal(" (vetor)", RecraftOperations.NameSuffix(RecraftOperation.Vectorize));
        }

        [Fact]
        public void LogName_IsStableAndGreppable()
        {
            Assert.Equal("remover-fundo", RecraftOperations.LogName(RecraftOperation.RemoveBackground));
            Assert.Equal("vetorizar", RecraftOperations.LogName(RecraftOperation.Vectorize));
        }
    }
}