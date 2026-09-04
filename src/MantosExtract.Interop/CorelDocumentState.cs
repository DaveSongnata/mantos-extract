using System;

namespace MantosExtract.Interop
{
    /// <summary>
    /// Switches the active document to millimetres and restores the original unit on
    /// <see cref="Dispose"/> — ALWAYS, even on error. Mirrors SisCut.Interop.CorelDocumentState
    /// exactly (each addin owns its own copy, no shared package between the sibling repos).
    /// Wrap any read/write of shape positions in a <c>using</c>.
    /// </summary>
    public sealed class CorelDocumentState : IDisposable
    {
        private readonly dynamic _document;
        private readonly int _originalUnit;
        private bool _restored;

        public CorelDocumentState(dynamic application)
        {
            _document = application.ActiveDocument;
            _originalUnit = (int)_document.Unit;
            _document.Unit = CorelConstants.CdrMillimeter;
        }

        public void Dispose()
        {
            if (_restored) return;
            _restored = true;
            try { _document.Unit = _originalUnit; } catch { /* document closed */ }
        }
    }
}
