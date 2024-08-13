#if TOOLS
using System;
using System.IO;
using System.Security.Cryptography;
using JWT.Algorithms;
using JWT.Builder;
using Microsoft.Data.Sqlite;
using Robust.Client.Utility;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Robust.Client.Console.Commands
{
    internal sealed class LauncherAuthCommand : LocalizedCommands
    {
        [Dependency] private readonly IAuthManager _auth = default!;
        [Dependency] private readonly IGameControllerInternal _gameController = default!;

        public override string Command => "launchauth";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var wantName = args.Length > 0 ? args[0] : null;

            var basePath = Path.GetDirectoryName(UserDataDir.GetUserDataDir(_gameController))!;
            //var dbPath = Path.Combine(basePath, "launcher-ssmv", "settings.db");
            var dbPath = Path.Combine(basePath, "Test61", "settings.db"); // TEMP

#if USE_SYSTEM_SQLITE
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#endif
            using var con = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT UserName, PublicKey, PrivateKey FROM LoginMV";

            if (wantName != null)
            {
                cmd.CommandText += " WHERE UserName = @userName";
                cmd.Parameters.AddWithValue("@userName", wantName);
            }

            cmd.CommandText += " LIMIT 1;";

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
            {
                shell.WriteLine("Unable to find a matching login");
                return;
            }

            var userName = reader.GetString(0);
            var publicKeyString = reader.GetString(1);
            var privateKeyString = reader.GetString(2);

            var publicKey = ECDsa.Create();
            publicKey.ImportFromPem(publicKeyString);

            var privateKey = ECDsa.Create();
            privateKey.ImportFromPem(privateKeyString);

            // Create JWT
            var token = JwtBuilder.Create()
                      .WithAlgorithm(new ES256Algorithm(publicKey, privateKey))
                      .AddClaim("exp", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()) // expiry
                      .AddClaim("nbf", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds()) // not before
                      .AddClaim("iat", DateTimeOffset.UtcNow) // issued at
                      .AddClaim("jti", "TODO") // TODO
                      .AddClaim("aud", "TODO") // TODO
                      .AddClaim("preferredUserName", userName)
                      .Encode();

            _auth.UserJWT = token;
            _auth.UserPublicKey = publicKeyString;

            shell.WriteLine($"Set auth parameters based on launcher keys for {userName}");
        }
    }
}

#endif
