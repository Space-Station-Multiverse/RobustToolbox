using System;
using System.Diagnostics.CodeAnalysis;

namespace Robust.Shared.Network
{
    // Basically turbo-lightweight IConfigurationManager for the purposes of auth var loading from env.

    /// <summary>
    ///     Stores client authentication parameters.
    /// </summary>
    internal interface IAuthManager
    {
        string? ServerPublicKey { get; set; }
        string? UserPublicKey { get; set; }
        string? UserJWT { get; set; }
        string? SharedSecretBase64 { get; set; }

        void LoadFromEnv();
    }

    internal sealed class AuthManager : IAuthManager
    {
        public string? ServerPublicKey { get; set; }
        public string? UserPublicKey { get; set; }
        public string? UserJWT { get; set; }
        public string? SharedSecretBase64 { get; set; }

        public void LoadFromEnv()
        {
            if (TryGetVar("ROBUST_AUTH_PUBKEY", out var pubKey)) // Server's public key
            {
                ServerPublicKey = pubKey;
            }

            if (TryGetVar("ROBUST_USER_PUBLIC_KEY", out var userPublicKey)) // User's public key
            {
                UserPublicKey = userPublicKey;
            }

            if (TryGetVar("ROBUST_USER_JWT", out var userJWT))
            {
                UserJWT = userJWT;
            }

            if (TryGetVar("ROBUST_SHARED_SECRET", out var sharedSecretBase64))
            {
                SharedSecretBase64 = sharedSecretBase64;
            }

            static bool TryGetVar(string var, [NotNullWhen(true)] out string? val)
            {
                val = Environment.GetEnvironmentVariable(var);
                return val != null;
            }
        }
    }
}
