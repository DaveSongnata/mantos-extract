using System;

namespace MantosExtract.Core.Auth
{
    /// <summary>
    /// Thrown by <see cref="IMantosfcAuthClient"/> on any failure. <see cref="Message"/> is
    /// already pt-BR and safe to show verbatim: for a server-side rejection it is the mantosfc
    /// response's own <c>message</c> field (the AdonisJS controllers already answer in pt-BR —
    /// verified in creator_auth_controller.ts), and only the network/malformed-response cases
    /// get a message written here.
    /// </summary>
    public sealed class MantosfcAuthException : Exception
    {
        /// <summary>Machine-readable code (<c>E_UNAUTHORIZED</c>, <c>E_IP_DENIED</c>,
        /// <c>E_TIME_DENIED</c>, <c>E_ACCOUNT_INACTIVE</c>, <c>E_ACCOUNT_BLOCKED</c>,
        /// <c>E_UNSUPPORTED_ROLE</c>, <c>E_NETWORK</c>, <c>E_MALFORMED_RESPONSE</c>,
        /// <c>E_UNKNOWN</c>). Drives which screen/branch the orchestrator takes — never shown
        /// to the operator directly.</summary>
        public string Code { get; }

        public MantosfcAuthException(string code, string message) : base(message)
        {
            Code = code;
        }

        public MantosfcAuthException(string code, string message, Exception inner) : base(message, inner)
        {
            Code = code;
        }
    }
}
