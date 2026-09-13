using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MantosExtract.Core.Upscale;

namespace MantosExtract.Windows
{
    /// <summary>
    /// Lança o binário do upscale com o token rebaixado para integridade MÉDIA quando o processo
    /// atual (o CorelDRAW) está com integridade ALTA. Motivo: o loader do Vulkan ignora
    /// VK_DRIVER_FILES/VK_ICD_FILENAMES se <c>integrity_level >= SECURITY_MANDATORY_HIGH_RID</c>
    /// (loader_environment.c, função is_high_integrity) — e é por essa variável que o modo CPU aponta
    /// o lavapipe. Corel elevado, ou VM com UAC desligado (todo processo nasce alto), faz o filho
    /// herdar a integridade alta e o modo CPU morrer em "vkCreateInstance failed -9".
    ///
    /// Integridade é atributo do TOKEN, não do UAC: dá pra rebaixar mesmo com UAC desligado.
    /// A saída do filho vai pra um arquivo via redirecionamento do cmd (sem herança de handle, que
    /// não é suportada pelo fallback CreateProcessWithTokenW), e o filho roda num Job Object com
    /// kill-on-close, pra timeout matar o exe e não só o cmd.
    /// </summary>
    public sealed class MediumIntegrityLauncher : IChildProcessLauncher
    {
        public const int LowRid = 0x1000;
        public const int MediumRid = 0x2000;
        public const int HighRid = 0x3000;

        private readonly bool _force;
        private readonly int _targetRid;
        private readonly string? _logDirectory;

        public MediumIntegrityLauncher() : this(false, MediumRid, null) { }

        /// <summary>Uso em teste: <paramref name="force"/> faz agir mesmo sem integridade alta, e
        /// <paramref name="targetRid"/> Low permite provar que o rebaixamento de fato acontece.</summary>
        public MediumIntegrityLauncher(bool force, int targetRid, string? logDirectory)
        {
            _force = force;
            _targetRid = targetRid;
            _logDirectory = logDirectory;
        }

        public static int CurrentIntegrityRid()
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token)) return -1;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int needed);
                IntPtr buf = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buf, needed, out _)) return -1;
                    IntPtr sid = Marshal.ReadIntPtr(buf); // TOKEN_MANDATORY_LABEL.Label.Sid
                    int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return Marshal.ReadInt32(GetSidSubAuthority(sid, count - 1));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return -1; }
            finally { if (token != IntPtr.Zero) CloseHandle(token); }
        }

        public static string DescribeCurrentIntegrity()
        {
            int rid = CurrentIntegrityRid();
            string name = rid >= 0x4000 ? "SISTEMA" : rid >= HighRid ? "ALTA (elevado)" : rid >= MediumRid ? "media" : rid >= 0 ? "baixa" : "desconhecida";
            return name + " (0x" + rid.ToString("X") + ")";
        }

        public ChildProcessResult? TryRun(ProcessStartInfo psi, int timeoutSeconds)
        {
            int current = CurrentIntegrityRid();
            if (!_force && current < HighRid) return null;

            string logDir = string.IsNullOrWhiteSpace(_logDirectory)
                ? Path.Combine(Path.GetTempPath(), "MantosExtract", "upscale")
                : _logDirectory!;
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "child-" + Guid.NewGuid().ToString("N") + ".log");

            string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            string inner = "\"" + psi.FileName + "\" " + psi.Arguments + " > \"" + logPath + "\" 2>&1";
            var cmdLine = new StringBuilder("\"" + cmdExe + "\" /d /s /c \"" + inner + "\"");

            IntPtr token = IntPtr.Zero, dup = IntPtr.Zero, sid = IntPtr.Zero, label = IntPtr.Zero, env = IntPtr.Zero, job = IntPtr.Zero;
            var pi = new PROCESS_INFORMATION();
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT, out token))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
                if (!DuplicateTokenEx(token, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out dup))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");
                if (!ConvertStringSidToSid("S-1-16-" + _targetRid, out sid))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertStringSidToSid");

                int labelSize = Marshal.SizeOf(typeof(TOKEN_MANDATORY_LABEL)) + GetLengthSid(sid);
                label = Marshal.AllocHGlobal(labelSize);
                Marshal.StructureToPtr(new TOKEN_MANDATORY_LABEL { Sid = sid, Attributes = SE_GROUP_INTEGRITY }, label, false);
                if (!SetTokenInformation(dup, TokenIntegrityLevel, label, labelSize))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(IntegrityLevel)");

                env = BuildEnvironmentBlock(psi);
                var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), dwFlags = STARTF_USESHOWWINDOW, wShowWindow = 0 };
                uint flags = CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW | CREATE_SUSPENDED;
                string? workDir = string.IsNullOrEmpty(psi.WorkingDirectory) ? null : psi.WorkingDirectory;

                string api;
                if (CreateProcessAsUser(dup, null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, flags, env, workDir, ref si, out pi))
                {
                    api = "CreateProcessAsUser";
                }
                else
                {
                    int first = Marshal.GetLastWin32Error();
                    if (!CreateProcessWithTokenW(dup, 0, null, cmdLine, flags, env, workDir, ref si, out pi))
                    {
                        int second = Marshal.GetLastWin32Error();
                        throw new Win32Exception(second,
                            "não consegui lançar o upscale com integridade rebaixada: CreateProcessAsUser erro " + first +
                            " (" + new Win32Exception(first).Message + "), CreateProcessWithTokenW erro " + second);
                    }
                    api = "CreateProcessWithTokenW (CreateProcessAsUser falhou: " + first + ")";
                }

                job = CreateJobObject(IntPtr.Zero, null);
                string jobNote = "";
                if (job != IntPtr.Zero)
                {
                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr infoPtr = Marshal.AllocHGlobal(len);
                    try
                    {
                        Marshal.StructureToPtr(info, infoPtr, false);
                        SetInformationJobObject(job, JobObjectExtendedLimitInformation, infoPtr, len);
                    }
                    finally { Marshal.FreeHGlobal(infoPtr); }
                    if (!AssignProcessToJobObject(job, pi.hProcess)) jobNote = " job=nao-atribuido(" + Marshal.GetLastWin32Error() + ")";
                }
                ResumeThread(pi.hThread);

                var result = new ChildProcessResult
                {
                    LaunchNote = "integridade rebaixada " + DescribeRid(current) + " -> " + DescribeRid(_targetRid) + " via " + api + jobNote,
                };

                uint wait = WaitForSingleObject(pi.hProcess, (uint)Math.Min(int.MaxValue, (long)timeoutSeconds * 1000));
                if (wait == WAIT_TIMEOUT)
                {
                    result.TimedOut = true;
                    if (job != IntPtr.Zero) TerminateJobObject(job, 1); else TerminateProcess(pi.hProcess, 1);
                    WaitForSingleObject(pi.hProcess, 5000);
                }
                else
                {
                    GetExitCodeProcess(pi.hProcess, out uint code);
                    result.ExitCode = unchecked((int)code);
                }

                ReadLog(logPath, result.OutputLines);
                return result;
            }
            finally
            {
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                if (job != IntPtr.Zero) CloseHandle(job);
                if (env != IntPtr.Zero) Marshal.FreeHGlobal(env);
                if (label != IntPtr.Zero) Marshal.FreeHGlobal(label);
                if (sid != IntPtr.Zero) LocalFree(sid);
                if (dup != IntPtr.Zero) CloseHandle(dup);
                if (token != IntPtr.Zero) CloseHandle(token);
                try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }
            }
        }

        private static string DescribeRid(int rid) =>
            (rid >= HighRid ? "alta" : rid >= MediumRid ? "media" : rid >= 0 ? "baixa" : "?") + "(0x" + rid.ToString("X") + ")";

        private static void ReadLog(string path, List<string> into)
        {
            try
            {
                if (!File.Exists(path)) { into.Add("(o processo não gerou log de saída)"); return; }
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, Encoding.Default))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null) into.Add(line);
                }
            }
            catch (Exception ex) { into.Add("(não consegui ler a saída do processo: " + ex.Message + ")"); }
        }

        /// <summary>Bloco de ambiente Unicode ("K=V\0...\0\0"), ordenado sem diferenciar caixa, como
        /// o CreateProcess exige — com as variáveis que o chamador pôs no ProcessStartInfo.</summary>
        private static IntPtr BuildEnvironmentBlock(ProcessStartInfo psi)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (DictionaryEntry e in psi.EnvironmentVariables)
                pairs.Add(new KeyValuePair<string, string>((string)e.Key, (string?)e.Value ?? ""));
            pairs.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));

            var sb = new StringBuilder();
            foreach (var p in pairs) sb.Append(p.Key).Append('=').Append(p.Value).Append('\0');
            sb.Append('\0');
            return Marshal.StringToHGlobalUni(sb.ToString());
        }

        // ---- P/Invoke ------------------------------------------------------------------------------

        private const uint TOKEN_ASSIGN_PRIMARY = 0x0001, TOKEN_DUPLICATE = 0x0002, TOKEN_QUERY = 0x0008, TOKEN_ADJUST_DEFAULT = 0x0080;
        private const uint MAXIMUM_ALLOWED = 0x02000000;
        private const int SecurityImpersonation = 2, TokenPrimary = 1, TokenIntegrityLevel = 25;
        private const uint SE_GROUP_INTEGRITY = 0x00000020;
        private const uint CREATE_SUSPENDED = 0x00000004, CREATE_UNICODE_ENVIRONMENT = 0x00000400, CREATE_NO_WINDOW = 0x08000000;
        private const int STARTF_USESHOWWINDOW = 0x00000001;
        private const uint WAIT_TIMEOUT = 0x00000102;
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_MANDATORY_LABEL { public IntPtr Sid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit;
            public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attrs, int impersonation, int type, out IntPtr newToken);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int length, out int returned);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(IntPtr token, int cls, IntPtr info, int length);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool ConvertStringSidToSid(string sid, out IntPtr psid);
        [DllImport("advapi32.dll")] private static extern int GetLengthSid(IntPtr sid);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, int index);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessAsUser(IntPtr token, string? app, StringBuilder cmdLine, IntPtr pa, IntPtr ta,
            bool inherit, uint flags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string? app, StringBuilder cmdLine,
            uint flags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, int length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(IntPtr job, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    }
}
