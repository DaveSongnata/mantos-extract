using System.Collections.Generic;
using MantosExtract.Core.Layout;
using Xunit;

namespace MantosExtract.Core.Tests.Layout
{
    public class ImportedShapeResolverTests
    {
        [Fact]
        public void FindsTheOnlyNewId_WhereverCorelPutsItInTheList()
        {
            var before = new HashSet<int> { 10, 11, 12 };
            Assert.Equal(0, ImportedShapeResolver.IndexOfNewShape(before, new List<int> { 99, 10, 11, 12 }));
            Assert.Equal(3, ImportedShapeResolver.IndexOfNewShape(before, new List<int> { 10, 11, 12, 99 }));
            Assert.Equal(1, ImportedShapeResolver.IndexOfNewShape(before, new List<int> { 10, 99, 11, 12 }));
        }

        [Fact]
        public void NeverReturnsAShapeThatAlreadyExisted()
        {
            // O bug real: a peça anterior (el_1) estava selecionada e foi tomada pela importada.
            var before = new HashSet<int> { 10, 11 };
            Assert.Equal(-1, ImportedShapeResolver.IndexOfNewShape(before, new List<int> { 10, 11 }));
            Assert.Equal(-1, ImportedShapeResolver.IndexOfNewShape(before, new List<int> { 11, 10 }, preferredIndex: 0));
        }

        [Fact]
        public void PrefersTheHintedIndex_OnlyIfItIsNew()
        {
            var before = new HashSet<int> { 1 };
            var after = new List<int> { 1, 50, 60 };
            Assert.Equal(2, ImportedShapeResolver.IndexOfNewShape(before, after, preferredIndex: 2));
            Assert.Equal(1, ImportedShapeResolver.IndexOfNewShape(before, after, preferredIndex: 0));
        }

        [Fact]
        public void EmptyLayerBeforeImport_TakesTheShapeThatAppeared()
        {
            Assert.Equal(0, ImportedShapeResolver.IndexOfNewShape(new HashSet<int>(), new List<int> { 7 }));
        }
    }
}
