using MantosExtract.Core.Extract;
using Xunit;

namespace MantosExtract.Core.Tests.Extract
{
    public class RefineSelectionTests
    {
        [Theory]
        [InlineData("target", RefineSlot.Target)]
        [InlineData("reference", RefineSlot.Reference)]
        public void TryParseSlot_KnowsBothSlots(string raw, RefineSlot expected)
        {
            Assert.True(RefineSelection.TryParseSlot(raw, out RefineSlot slot));
            Assert.Equal(expected, slot);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Target")]
        [InlineData("source")]
        public void TryParseSlot_RejectsAnythingElse(string? raw)
        {
            Assert.False(RefineSelection.TryParseSlot(raw, out _));
        }

        [Fact]
        public void NewSelection_HasNoTarget_AndCannotRun()
        {
            var selection = new RefineSelection();

            Assert.Null(selection.Target);
            Assert.Null(selection.Reference);
            Assert.Equal(RefineSelection.NoTargetCode, selection.ValidateForRun());
        }

        [Fact]
        public void WithOnlyATarget_ItRuns_WithoutReference()
        {
            var selection = new RefineSelection();

            Assert.Null(selection.Set(RefineSlot.Target, new byte[] { 1, 2 }));

            Assert.Null(selection.ValidateForRun());
            Assert.Null(selection.Reference);
        }

        [Fact]
        public void TargetAndReference_AreKeptSeparately()
        {
            var selection = new RefineSelection();

            selection.Set(RefineSlot.Target, new byte[] { 1 });
            selection.Set(RefineSlot.Reference, new byte[] { 2 });

            Assert.Equal(new byte[] { 1 }, selection.Target);
            Assert.Equal(new byte[] { 2 }, selection.Reference);
            Assert.Null(selection.ValidateForRun());
        }

        [Fact]
        public void AReferenceIdenticalToTheTarget_IsRefused_AndTheSlotStaysAsItWas()
        {
            var selection = new RefineSelection();
            selection.Set(RefineSlot.Target, new byte[] { 1, 2, 3 });

            // Operador esqueceu de trocar a seleção no Corel: mandaria a mesma imagem duas vezes.
            string? code = selection.Set(RefineSlot.Reference, new byte[] { 1, 2, 3 });

            Assert.Equal(RefineSelection.SameImageCode, code);
            Assert.Null(selection.Reference);
        }

        [Fact]
        public void ATargetIdenticalToTheReference_IsRefusedToo_KeepingThePreviousTarget()
        {
            var selection = new RefineSelection();
            selection.Set(RefineSlot.Target, new byte[] { 1 });
            selection.Set(RefineSlot.Reference, new byte[] { 2 });

            string? code = selection.Set(RefineSlot.Target, new byte[] { 2 });

            Assert.Equal(RefineSelection.SameImageCode, code);
            Assert.Equal(new byte[] { 1 }, selection.Target);
        }

        [Fact]
        public void ReplacingASlot_KeepsTheOther()
        {
            var selection = new RefineSelection();
            selection.Set(RefineSlot.Target, new byte[] { 1 });
            selection.Set(RefineSlot.Reference, new byte[] { 2 });

            selection.Set(RefineSlot.Target, new byte[] { 3 });

            Assert.Equal(new byte[] { 3 }, selection.Target);
            Assert.Equal(new byte[] { 2 }, selection.Reference);
        }

        [Fact]
        public void ClearingTheReference_KeepsTheTarget()
        {
            var selection = new RefineSelection();
            selection.Set(RefineSlot.Target, new byte[] { 1 });
            selection.Set(RefineSlot.Reference, new byte[] { 2 });

            selection.Clear(RefineSlot.Reference);

            Assert.Null(selection.Reference);
            Assert.Equal(new byte[] { 1 }, selection.Target);
        }

        [Fact]
        public void Reset_EmptiesBothSlots()
        {
            // Abrir o modal de novo nunca pode herdar a referência de outro trabalho.
            var selection = new RefineSelection();
            selection.Set(RefineSlot.Target, new byte[] { 1 });
            selection.Set(RefineSlot.Reference, new byte[] { 2 });

            selection.Reset();

            Assert.Null(selection.Target);
            Assert.Null(selection.Reference);
        }
    }
}
