using System;

namespace MantosExtract.Core.Extract
{
    /// <summary>
    /// Folder name for one extraction batch — filesystem-as-database (Dave, 2026-09-07): no
    /// database, the folder name plus a manifest.json inside it (ExtractionBatchManifest) are
    /// the full source of truth for the history screen. Sortable by date, unique (batch id
    /// prefix), and shows the element count at a glance without opening anything.
    /// </summary>
    public static class ExtractionBatchNaming
    {
        public static string FolderName(Guid batchId, DateTime timestamp, int totalElements)
        {
            string datePart = timestamp.ToString("yyyyMMdd-HHmmss");
            string idPart = batchId.ToString("N").Substring(0, 8);
            return $"{datePart}_{idPart}_{totalElements}el";
        }
    }
}
