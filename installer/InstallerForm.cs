using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MantosExtract.Installer.Core;

namespace MantosExtract.Installer;

public class InstallerForm : Form
{
    // Borderless-window dragging: WebView2 captures the mouse, so the HTML title bar posts an
    // {action:'drag'} and we start the native move here (ReleaseCapture + WM_NCLBUTTONDOWN).
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;
    // Rounded corners on Windows 11 (no-op on older Windows).
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };

    public InstallerForm()
    {
        Text = "Mantos Extract — Instalador";
        Width = 900; Height = 600;
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.None;   // the HTML draws its own clean title bar
        Icon = LoadAppIcon();
        ShowInTaskbar = true;
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
        HandleCreated += (_, _) => ApplyRoundedCorners();
    }

    // "app.icon.ico" é embutido pelo .csproj a partir de
    // ../src/MantosExtract.Resources/icons/mantosextract.ico (arte aprovada, 2026-09-04).
    // GetManifestResourceStream devolve null pra um recurso ausente (não lança), então isto
    // segue degradando de forma limpa pro ícone padrão do WinForms se o recurso um dia sumir.
    private static Icon? LoadAppIcon()
    {
        try
        {
            using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.icon.ico");
            return s != null ? new Icon(s) : null;
        }
        catch { return null; }
    }

    private void ApplyRoundedCorners()
    {
        try { int pref = DWMWCP_ROUND; DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch { }
    }

    private async Task InitAsync()
    {
        try
        {
            string cacheDir = Path.Combine(Path.GetTempPath(), "MantosExtract_Installer_WebView2Cache");
            var env = await CoreWebView2Environment.CreateAsync(null, cacheDir);
            await _web.EnsureCoreWebView2Async(env);

            string uiDir = ExtractUiToTemp();
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "installer.mantosextract.app", uiDir, CoreWebView2HostResourceAccessKind.Allow);

            _web.CoreWebView2.WebMessageReceived += OnMessage;
            _web.Source = new Uri("https://installer.mantosextract.app/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "O WebView2 Runtime não foi encontrado.\n\n" +
                "Instale o Microsoft Edge WebView2 Runtime e tente novamente.\n" +
                "Download: https://developer.microsoft.com/microsoft-edge/webview2/",
                "Mantos Extract — Requisito ausente",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private static string ExtractUiToTemp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MantosExtract_Installer_ui");
        Directory.CreateDirectory(dir);
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream("ui.index.html")
            ?? throw new InvalidOperationException("Embedded resource not found: ui.index.html");
        using var fs = File.Create(Path.Combine(dir, "index.html"));
        s.CopyTo(fs);
        return dir;
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try { msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement; }
        catch { return; }

        string action = msg.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        switch (action)
        {
            case "install":  await RunInstallAsync(); break;
            case "close":    Close(); break;
            case "drag":     ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0); break;
            case "minimize": WindowState = FormWindowState.Minimized; break;
        }
    }

    private async Task RunInstallAsync()
    {
        try
        {
            var progress = new Progress<InstallProgress>(p =>
                BeginInvoke((Action)(() => Send($"{{\"type\":\"progress\",\"percent\":{p.Percent}}}"))));

            await Task.Run(() => new InstallerEngine(progress).Run());

            Send("{\"type\":\"done\",\"success\":true}");
        }
        catch (Exception ex)
        {
            Send($"{{\"type\":\"done\",\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}");
        }
    }

    private void Send(string json) => _web.CoreWebView2?.PostWebMessageAsJson(json);

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}
