using System;

namespace MantosExtract.Interop
{
    /// <summary>
    /// Log sink for the COM layer. Interop has no file logger of its own; the add-in wires
    /// <see cref="Sink"/> to docker.log. Exists so every silent decision here (which shape was
    /// taken as "imported", whether a tracked reference was dead, which export path produced the
    /// file, exceptions a catch swallows on purpose) shows up in the field log.
    /// </summary>
    public static class InteropLog
    {
        public static Action<string>? Sink { get; set; }

        public static void Write(string message)
        {
            try { Sink?.Invoke("[corel] " + message); } catch { /* logging must never break the host */ }
        }

        /// <summary>One-line exception: type, HRESULT and message of the innermost COM error.</summary>
        public static string Describe(Exception ex)
        {
            Exception e = ex;
            while (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + " 0x" + e.HResult.ToString("X8") + ": " + e.Message;
        }

        /// <summary>StaticID, type, name and bounds (mm while CorelDocumentState is active) of a
        /// shape, never throwing — each field reads independently.</summary>
        public static string ShapeInfo(object? shape)
        {
            if (shape == null) return "(null)";
            dynamic s = shape;
            string id = "?", type = "?", name = "?", box = "?";
            try { id = ((int)s.StaticID).ToString(); } catch (Exception ex) { id = "ilegível(" + Describe(ex) + ")"; }
            try { type = ((int)s.Type).ToString(); } catch { }
            try { name = "\"" + (string)s.Name + "\""; } catch { }
            try
            {
                box = string.Format(System.Globalization.CultureInfo.InvariantCulture, "x={0:0.#} y={1:0.#} {2:0.#}x{3:0.#}",
                    (double)s.LeftX, (double)s.BottomY, (double)s.SizeWidth, (double)s.SizeHeight);
            }
            catch { }
            return "id=" + id + " tipo=" + type + " nome=" + name + " " + box;
        }

        /// <summary>Pixel size read from a PNG's IHDR (bytes 16-23), or a note if unreadable.</summary>
        public static string PngInfo(string path)
        {
            try
            {
                var fi = new System.IO.FileInfo(path);
                if (!fi.Exists) return "arquivo inexistente";
                using var fs = System.IO.File.OpenRead(path);
                var h = new byte[26];
                if (fs.Read(h, 0, 26) < 26) return fi.Length + " bytes (cabeçalho curto)";
                int w = (h[16] << 24) | (h[17] << 16) | (h[18] << 8) | h[19];
                int hh = (h[20] << 24) | (h[21] << 16) | (h[22] << 8) | h[23];
                // IHDR color type: 2 = RGB, 6 = RGBA (4 = cinza+alpha, 0 = cinza, 3 = paleta).
                string color = h[25] == 6 ? "RGBA" : h[25] == 2 ? "RGB" : "colorType=" + h[25];
                return w + "x" + hh + "px " + color + " " + fi.Length + " bytes";
            }
            catch (Exception ex) { return "ilegível (" + Describe(ex) + ")"; }
        }
    }
}
