using System;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace MantosExtract.AddIn.Ui
{
    /// <summary>
    /// DEVELOPMENT ONLY (ported from AiSten/SisCut, 2026-10-13): when
    /// <c>%LOCALAPPDATA%\MantosExtract\dev-tools.on</c> exists, the docker's WebView2 opens the
    /// Chrome DevTools Protocol on 127.0.0.1:<see cref="Port"/>, so an agent/test on the dev
    /// machine can click and read the REAL page inside CorelDRAW (tools/corel-com/cdp.mjs).
    /// Customer PCs never have the file, so nothing changes there. Ports already taken by the
    /// sibling dockers that share this Corel: AiSten 9477, SisCut 9478.
    /// </summary>
    internal static class MantosExtractDevTools
    {
        public const int Port = 9479;

        public static CoreWebView2EnvironmentOptions? EnvironmentOptions()
        {
            try
            {
                string flag = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantosExtract", "dev-tools.on");
                if (!File.Exists(flag)) return null;
                MantosExtractLog.Write("DEV: porta de depuração do WebView2 ligada em 127.0.0.1:" + Port + " (dev-tools.on)");
                return new CoreWebView2EnvironmentOptions(
                    "--remote-debugging-port=" + Port + " --remote-allow-origins=*");
            }
            catch { return null; }
        }
    }
}
