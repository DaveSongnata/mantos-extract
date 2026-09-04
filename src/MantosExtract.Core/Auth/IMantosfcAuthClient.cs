using System.Threading;
using System.Threading.Tasks;

namespace MantosExtract.Core.Auth
{
    /// <summary>
    /// Everything Fase 1 needs from mantosfc. Segregated behind an interface so
    /// <see cref="AuthOrchestrator"/> is testable with a fake — no real HTTP, no real server —
    /// same shape as SisCut.Security.LicenseClient but for the creator/tenant Bearer session,
    /// never the HWID+Ed25519 desktop-license flow (those are deliberately separate systems).
    /// </summary>
    public interface IMantosfcAuthClient
    {
        Task<LoginResponse> LoginAsync(string email, string password, CancellationToken ct);
        Task<MeResponse> GetMeAsync(string sessionId, CancellationToken ct);
        Task LogoutAsync(string sessionId, CancellationToken ct);

        /// <summary>Troca voluntária de senha (Configurações), disponível a qualquer momento —
        /// NUNCA forçada no primeiro login (decisão do Dave, 2026-09-04: "so vamos
        /// disponibilizar"). Backend já existe pronto, sem mudança nenhuma no mantosfc:
        /// POST /api/v1/creator/me/change-password (verificado em me_controller.ts).</summary>
        Task ChangePasswordAsync(string sessionId, string currentPassword, string newPassword, CancellationToken ct);
    }
}
