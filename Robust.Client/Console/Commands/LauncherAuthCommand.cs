#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

            using var con = GetDb();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT UserName, PublicKey, PrivateKey FROM LoginMVKey";

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
                      .AddClaim("aud", "TODO") // TODO
                      .AddClaim("preferredUserName", userName)
                      .Encode();

            _auth.UserJWT = token;
            _auth.UserPublicKey = publicKeyString;

            shell.WriteLine($"Set auth parameters based on launcher keys for {userName}");
        }

        public override async ValueTask<CompletionResult> GetCompletionAsync(
            IConsoleShell shell,
            string[] args,
            string argStr,
            CancellationToken cancel)
        {
            if (args.Length != 1)
                return CompletionResult.Empty;

            return await Task.Run(() =>
                {
                    using var con = GetDb();

                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "SELECT UserName FROM Login WHERE Expires > datetime('NOW')";

                    var options = new List<CompletionOption>();

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var name = reader.GetString(0);
                        options.Add(new CompletionOption(name));
                    }

                    return CompletionResult.FromOptions(options);
                },
                cancel);
        }

        private SqliteConnection GetDb()
        {
            var basePath = UserDataDir.GetRootUserDataDir(_gameController);
            var launcherDirName = Environment.GetEnvironmentVariable("SSMV_LAUNCHER_APPDATA_NAME") ?? "launcher";
            var dbPath = Path.Combine(basePath, launcherDirName, "settings.db");

#if USE_SYSTEM_SQLITE
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#endif
            var con = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            con.Open();

            return con;
        }
    }
}

#endif
