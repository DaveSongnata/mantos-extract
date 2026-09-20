namespace MantosExtract.Interop
{
    /// <summary>
    /// The seam between the bridge and CorelDRAW's COM automation. Everything that touches the
    /// running CorelDRAW instance goes through this interface — same shape as SisCut.AddIn's
    /// ICorelHost.
    /// </summary>
    public interface ICorelHost
    {
        /// <summary>Active document's display name, or a placeholder when none is open.</summary>
        string DocumentName { get; }

        /// <summary>Number of shapes in the active selection (0 if nothing/closed doc).</summary>
        int SelectionShapeCount { get; }

        /// <summary>True only when the selection is EXACTLY one bitmap shape — the only
        /// selection state "Detectar" (Fase 2) is enabled for.</summary>
        bool SelectionIsSingleBitmap { get; }

        /// <summary>Exports the current selection to <paramref name="path"/> as PNG. Throws a
        /// message already safe to log/relay on failure.</summary>
        void ExportSelectionToPng(string path, int dpi);

        /// <summary>Imports <paramref name="pngPath"/> into the active layer at whatever
        /// position/size Corel gives it natively. Returns (LeftX, BottomY, SizeWidth,
        /// SizeHeight) so the caller can compute a real layout slot (Core.Layout.ElementLayout,
        /// pure/testable) BEFORE moving it — the size isn't known until after import.</summary>
        (double LeftMm, double BottomMm, double WidthMm, double HeightMm) ImportPng(string pngPath);

        /// <summary>Moves the most recently imported shape's bottom-left corner to
        /// (<paramref name="leftMm"/>, <paramref name="bottomMm"/>) in document millimetres.</summary>
        void MoveLastImportedShape(double leftMm, double bottomMm);

        /// <summary>Renames the most recently imported shape — sanitized by the caller before
        /// it ever reaches here (docs/mantos-extract-spec.md §6, "José &amp; Cia" case).</summary>
        void RenameLastImportedShape(string safeName);

        /// <summary>Remembers the shape just imported under <paramref name="key"/>, so a later
        /// optional upscale (Dave, 2026-09-11) can swap THAT specific shape rather than whatever
        /// happens to be selected when the operator clicks the button.</summary>
        void TrackLastImportedShape(string key);

        /// <summary>Swaps the shape remembered under <paramref name="key"/> for
        /// <paramref name="pngPath"/>, keeping the original's position AND physical size — an
        /// upscale adds pixels, it must not resize the artwork on the page. Returns false when
        /// the original is gone (operator deleted it by hand), so the caller can say so instead
        /// of silently dropping a second copy on the canvas.</summary>
        bool ReplaceTrackedShape(string key, string pngPath);

        /// <summary>Upscale avulso (Dave, 2026-09-13 — "só fazer upscale de um elemento, sem
        /// regerar"): grava o bitmap ÚNICO selecionado em <paramref name="pngPath"/> na resolução
        /// NATIVA e com transparência (IVGBitmap.SaveAs, não a exportação da página, que reamostra
        /// pelo dpi e sai opaca) e passa a rastrear esse shape sob <paramref name="trackKey"/> pra
        /// que ReplaceTrackedShape o troque depois. Lança se a seleção não for um bitmap só.</summary>
        /// <returns>Linha pro log: atributos do bitmap e por qual via o arquivo saiu.</returns>
        string SaveSelectedBitmapForUpscale(string pngPath, string trackKey);

        /// <summary>Refino por prompt livre (Dave, 2026-09-18): importa <paramref name="pngPath"/> e
        /// coloca AO LADO da forma rastreada sob <paramref name="key"/> (Core.Layout.RefinePlacement),
        /// com o MESMO tamanho físico em mm e o nome dela + <paramref name="nameSuffix"/>. O original
        /// não é tocado — fica recuperável. Devolve false, sem importar nada, quando o original foi
        /// apagado à mão pelo operador.</summary>
        /// <param name="isVector">true = o arquivo é um SVG (vetorizar, Recraft — Dave 2026-09-20):
        /// importa pelo filtro SVG, agrupa se vier em vários objetos e encaixa por escala UNIFORME
        /// dentro da caixa do original (o SVG não tem a proporção exata do bitmap).</param>
        bool PlaceBesideTrackedShape(string key, string pngPath, string nameSuffix, bool isVector = false);

        /// <summary>Active page bounding box in millimetres, used to place the first imported
        /// element sensibly when there is no prior element to lay out next to.</summary>
        (double LeftMm, double BottomMm, double WidthMm, double HeightMm) ActivePageBoundsMm();
    }
}
