using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MantosExtract.Core.Auth;

namespace MantosExtract.Windows
{
    /// <summary>
    /// DPAPI-backed <see cref="ICredentialStore"/> — the real implementation the add-in wires
    /// up; tests use an in-memory fake instead (Core stays pure, no Windows dependency there).
    ///
    /// <para>
    /// Two files under <c>%LOCALAPPDATA%\MantosExtract</c>, each individually
    /// <c>ProtectedData.Protect</c>'d with <see cref="DataProtectionScope.CurrentUser"/> (no
    /// extra entropy): the mantosfc session (Bearer UUID + expiry + role + email — never the
    /// password) and the tenant's own OpenAI key (BYOK, Dave 2026-09-03). CurrentUser scope
    /// already gives the guarantee we need — bound to this Windows user on this machine, unable
    /// to silently roam to another machine — so a second secret is complexity without a threat
    /// it defends against.
    /// </para>
    /// <para>
    /// Every operation is non-throwing on I/O/crypto failure (a locked-down profile loses the
    /// saved session/key, not the ability to open the docker) — same discipline as
    /// Optimus.Windows.LanguageStore.
    /// </para>
    /// </summary>
    public sealed class SecureCredentialStore : ICredentialStore
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantosExtract");

        private static string SessionPath => Path.Combine(Dir, "session.bin");
        private static string OpenAiKeyPath => Path.Combine(Dir, "openai.bin");

        public SessionState? LoadSession()
        {
            string? json = ReadProtected(SessionPath);
            if (json == null) return null;

            try { return JsonSerializer.Deserialize<SessionState>(json); }
            catch (JsonException) { return null; }
        }

        public void SaveSession(SessionState session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            WriteProtected(SessionPath, JsonSerializer.Serialize(session));
        }

        public void ClearSession() => DeleteQuietly(SessionPath);

        public string? LoadOpenAiKey() => ReadProtected(OpenAiKeyPath);

        public void SaveOpenAiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Chave vazia.", nameof(apiKey));
            WriteProtected(OpenAiKeyPath, apiKey.Trim());
        }

        public void ClearOpenAiKey() => DeleteQuietly(OpenAiKeyPath);

        private static string? ReadProtected(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] cipher = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception) { return null; }
        }

        private static void WriteProtected(string path, string plainText)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                byte[] plain = Encoding.UTF8.GetBytes(plainText);
                byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, cipher);
            }
            catch (Exception) { /* an unwritable profile costs the credential, not the app */ }
        }

        private static void DeleteQuietly(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { /* best effort */ }
        }
    }
}
