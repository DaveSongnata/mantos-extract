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
    /// Houve um terceiro arquivo, <c>recraft.bin</c> (BYOK da Recraft, 2026-09-20 a 2026-09-22).
    /// A chave da Recraft passou a ser única, no servidor; o arquivo herdado é apagado na
    /// construção desta classe em vez de ficar esquecido no perfil do operador.
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
        /// <summary>Cofre da chave BYOK da Recraft, que valeu de 2026-09-20 a 2026-09-22. Só existe
        /// aqui pra ser APAGADO — ver <see cref="PurgeLegacyRecraftKey"/>.</summary>
        private static string LegacyRecraftKeyPath => Path.Combine(Dir, "recraft.bin");

        public SecureCredentialStore()
        {
            PurgeLegacyRecraftKey();
        }

        /// <summary>
        /// Apaga a chave BYOK da Recraft que ficou no disco de quem atualizou de uma versão
        /// anterior à 0.9.12. A chave virou única da plataforma e o addin não a usa mais — deixar
        /// uma credencial viva do operador num arquivo que nada mais lê é dívida de segurança, não
        /// compatibilidade. Silencioso e não-lançante como todo o resto desta classe: falhar em
        /// limpar não pode impedir o docker de abrir.
        /// </summary>
        private static void PurgeLegacyRecraftKey() => DeleteQuietly(LegacyRecraftKeyPath);

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
