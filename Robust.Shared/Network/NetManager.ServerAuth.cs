﻿using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Exceptions;
using JWT.Serializers;
using Lidgren.Network;
using Robust.Shared.AuthLib;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network.Messages.Handshake;
using Robust.Shared.Utility;
using SpaceWizards.Sodium;

namespace Robust.Shared.Network
{
    partial class NetManager
    {
        private static readonly string DisconnectReasonWrongKey = new NetDisconnectMessage("Token decryption failed.\nPlease reconnect to this server from the launcher.", true).Encode();

        private readonly byte[] _cryptoPrivateKey = new byte[CryptoBox.SecretKeyBytes];

        public byte[] CryptoPublicKey { get; } = new byte[CryptoBox.PublicKeyBytes];
        public AuthMode Auth { get; private set; }

        public Func<string, Task<NetUserId?>>? AssignUserIdCallback { get; set; }
        public IServerNetManager.NetApprovalDelegate? HandleApprovalCallback { get; set; }

        private void SAGenerateKeys()
        {
            CryptoBox.KeyPair(CryptoPublicKey, _cryptoPrivateKey);

            _authLogger.Debug("Public key is {0}", Convert.ToBase64String(CryptoPublicKey));
        }

        private async void HandleHandshake(NetPeerData peer, NetConnection connection)
        {
            try
            {
                _logger.Verbose($"{connection.RemoteEndPoint}: Starting handshake with peer ");

                _logger.Verbose($"{connection.RemoteEndPoint}: Awaiting MsgLoginStart");
                var incPacket = await AwaitData(connection);

                var msgLogin = new MsgLoginStart();
                msgLogin.ReadFromBuffer(incPacket, _serializer);

                var ip = connection.RemoteEndPoint.Address;
                var isLocal = IPAddress.IsLoopback(ip) && _config.GetCVar(CVars.AuthAllowLocal);
                var canAuth = msgLogin.CanAuth;
                var needServerPublicKey = msgLogin.NeedServerPublicKey;

                _logger.Verbose(
                    $"{connection.RemoteEndPoint}: Received MsgLoginStart. " +
                    $"canAuth: {canAuth}, needServerPublicKey: {needServerPublicKey}, username: {msgLogin.PreferredUserName}, encrypt: {msgLogin.Encrypt}");

                _logger.Verbose(
                    $"{connection.RemoteEndPoint}: Connection is specialized local? {isLocal} ");

                if (Auth == AuthMode.Required && !isLocal)
                {
                    if (!canAuth)
                    {
                        connection.Disconnect("Connecting to this server requires authentication");
                        return;
                    }
                }

                NetEncryption? encryption = null;
                NetUserData userData;
                LoginType type;
                var padSuccessMessage = true;

                if (canAuth && Auth != AuthMode.Disabled)
                {
                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: Initiating authentication");

                    var verifyToken = new byte[4];
                    RandomNumberGenerator.Fill(verifyToken);
                    var msgEncReq = new MsgEncryptionRequest
                    {
                        PublicKey = needServerPublicKey ? CryptoPublicKey : Array.Empty<byte>(),
                        VerifyToken = verifyToken
                    };

                    var outMsgEncReq = peer.Peer.CreateMessage();
                    outMsgEncReq.Write(false);
                    outMsgEncReq.WritePadBits();
                    msgEncReq.WriteToBuffer(outMsgEncReq, _serializer);
                    peer.Peer.SendMessage(outMsgEncReq, connection, NetDeliveryMethod.ReliableOrdered);

                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: Awaiting MsgEncryptionResponse");

                    incPacket = await AwaitData(connection);

                    var msgEncResponse = new MsgEncryptionResponse();
                    msgEncResponse.ReadFromBuffer(incPacket, _serializer);

                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: Received MsgEncryptionResponse");

                    var encResp = new byte[verifyToken.Length + SharedKeyLength];
                    var ret = CryptoBox.SealOpen(
                        encResp,
                        msgEncResponse.SealedData,
                        CryptoPublicKey,
                        _cryptoPrivateKey);

                    if (!ret)
                    {
                        // Launcher gives the client the public RSA key of the server BUT
                        // that doesn't persist if the server restarts.
                        // In that case, the decrypt can fail here.
                        connection.Disconnect(DisconnectReasonWrongKey);
                        return;
                    }

                    // Data is [shared]+[verify]
                    var verifyTokenCheck = encResp[SharedKeyLength..];
                    var sharedSecret = encResp[..SharedKeyLength];

                    if (!verifyToken.AsSpan().SequenceEqual(verifyTokenCheck))
                    {
                        connection.Disconnect("Verify token is invalid");
                        return;
                    }

                    if (msgLogin.Encrypt)
                    {
                        encryption = new NetEncryption(sharedSecret, isServer: true);
                        encryption.SetNonce(msgEncResponse.StartingNonce);
                    }

                    var authHashBytes = MakeAuthHash(sharedSecret, CryptoPublicKey!);
                    var authHash = Base64Helpers.ConvertToBase64Url(authHashBytes);

                    // Validate the JWT
                    var userPublicKeyString = msgEncResponse.UserPublicKey ?? "";
                    var userJWTString = msgEncResponse.UserJWT ?? "";

                    var userPublicKey = ECDsa.Create();
                    userPublicKey.ImportFromPem(userPublicKeyString);

                    string jwtJsonString = "";

                    try
                    {
                        IJsonSerializer serializer = new JsonNetSerializer();
                        IDateTimeProvider provider = new UtcDateTimeProvider();
                        IJwtValidator validator = new JwtValidator(serializer, provider);
                        IBase64UrlEncoder urlEncoder = new JwtBase64UrlEncoder();
                        IJwtAlgorithm algorithm = new ES256Algorithm(userPublicKey);
                        IJwtDecoder decoder = new JwtDecoder(serializer, validator, urlEncoder, algorithm);

                        jwtJsonString = decoder.Decode(userJWTString);
                    }
                    catch (TokenNotYetValidException)
                    {
                        connection.Disconnect("JWT Validation Error - Token is not valid yet.");
                        return;
                    }
                    catch (TokenExpiredException)
                    {
                        connection.Disconnect("JWT Validation Error - Token has expired.");
                        return;
                    }
                    catch (SignatureVerificationException)
                    {
                        connection.Disconnect("JWT Validation Error - Token has invalid signature.");
                        return;
                    }
                    catch (Exception e)
                    {
                        connection.Disconnect("Misc JWT Error.");
                        _logger.Error("Misc JWT Error on user attempting to connect.", e);
                        return;
                    }

                    if (String.IsNullOrEmpty(jwtJsonString))
                    {
                        connection.Disconnect("JWT Validation Error - No JSON in JWT.");
                        return;
                    }

                    // Verify JWT is actually for this server
                    JsonNode? jsonNode = JsonNode.Parse(jwtJsonString);

                    if (jsonNode == null)
                    {
                        connection.Disconnect("JWT Validation Error - Bad/Missing JSON in JWT.");
                        return;
                    }

                    bool verifyAudienceClaim = true;

                    #if TOOLS
                    // Dev builds can skip this for ease of testing purposes.
                    verifyAudienceClaim = _config.GetCVar<bool>(CVars.AuthRequireAudienceClaim);
                    #endif

                    if (verifyAudienceClaim)
                    {
                        var audienceClaimNode = jsonNode["aud"];
                        if (audienceClaimNode == null)
                        {
                            connection.Disconnect("JWT Validation Error - No audience claim in JWT.");
                            return;
                        }

                        string signedForServerInJWT = audienceClaimNode.GetValue<string>();
                        string serverSignatureBase64 = Convert.ToBase64String(CryptoPublicKey);
                        if (signedForServerInJWT != serverSignatureBase64)
                        {
                            // It could just be that the server recently restarted and launcher has old key.
                            connection.Disconnect("JWT Validation Error\nJWT appears to be for another server.\nTry returning to launcher and reconnect.");
                            return;
                        }

                        // Also verify authhash matches.
                        // (This step helps deter a MITM/proxy attack, since even if traffic was proxied, it should also
                        // be encrypted.)
                        string authHashClaim = "";
                        var authHashClaimNode = jsonNode["authhash"];
                        if (authHashClaimNode == null)
                        {
                            connection.Disconnect("JWT Validation Error - No auth hash in JWT\n(Ensure you are using latest launcher version).");
                            return;
                        }
                        authHashClaim = (string) authHashClaimNode.GetValue<string>();
                        if (authHashClaim != authHash)
                        {
                            connection.Disconnect("JWT Validation Error - Wrong auth hash in JWT\n(Check server address is correct).");
                            return;
                        }
                    }

                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: JWT appears valid");

                    // Find user based on public key

                    // Get public key in byte format.  This should be a bit more efficient than
                    // doing lookups based on base64() keys, and by using the parsed ES256 object
                    // it should prevent any issues about slightly differing PEM syntax, etc. resulting
                    // in multiple versions of the same key.
                    var userPublicKeyX509Der = userPublicKey.ExportSubjectPublicKeyInfo();
                    var userPublicKeyImmutableBytes = userPublicKeyX509Der.ToImmutableArray();

                    var serverUserDataAssociation = IoCManager.Resolve<IServerUserDataAssociation>();
                    var associationResult = await serverUserDataAssociation.AttemptUserDataFromPublicKey(
                        userPublicKeyImmutableBytes, msgLogin.HWId, msgLogin.PreferredUserName, ip);

                    if (associationResult.success && associationResult.userData != null)
                    {
                        _logger.Verbose(
                         $"{connection.RemoteEndPoint}: Content successfully found/created user data in AttemptUserDataFromPublicKey.");

                         userData = associationResult.userData;
                    }
                    else
                    {
                        _logger.Verbose(
                         $"{connection.RemoteEndPoint}: Disconnecting due to Content AttemptUserDataFromPublicKey failing ({associationResult.errorMessage})");

                        connection.Disconnect($"There was a problem logging you in.  {associationResult.errorMessage}");
                        return;
                    }

                    padSuccessMessage = false;
                    type = LoginType.LoggedIn;
                }
                else
                {
                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: Not doing authentication");

                    var reqUserName = msgLogin.PreferredUserName;

                    if (!UsernameHelpers.IsNameValid(reqUserName, out var reason))
                    {
                        connection.Disconnect($"Username is invalid ({reason.ToText()}).");
                        return;
                    }

                    // If auth is set to "optional" we need to avoid conflicts between real accounts and guests,
                    // so we explicitly prefix guests.
                    var origName = Auth == AuthMode.Disabled
                        ? reqUserName
                        : (isLocal ? $"localhost@{reqUserName}" : $"guest@{reqUserName}");
                    var name = origName;
                    var iterations = 1;

                    while (_assignedUsernames.ContainsKey(name))
                    {
                        // This is shit but I don't care.
                        name = $"{origName}_{++iterations}";
                    }

                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: Assigned name: {name}");

                    NetUserId userId;
                    (userId, type) = await AssignUserIdAsync(name);

                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: Assigned user ID: {userId}");

                    userData = new NetUserData(userId, name)
                    {
                        HWId = msgLogin.HWId,
                        PublicKey = ImmutableArray<byte>.Empty
                    };
                }

                _logger.Verbose(
                    $"{connection.RemoteEndPoint}: Login type: {type}");

                _logger.Verbose(
                    $"{connection.RemoteEndPoint}: Raising Connecting event");

                var endPoint = connection.RemoteEndPoint;
                var connect = await OnConnecting(endPoint, userData, type);
                if (connect.DenyReasonData is { } deny)
                {
                    var denyMsg = $"Connect denied: {deny.Text}";
                    var structured = new NetDisconnectMessage(denyMsg);
                    foreach (var (k, v) in deny.AdditionalProperties)
                    {
                        structured.Values[k] = v;
                    }
                    connection.Disconnect(structured.Encode());
                    return;
                }

                _logger.Verbose(
                    $"{connection.RemoteEndPoint}: Connecting event passed, client is IN");

                // Well they're in. Kick a connected client with the same GUID if we have to.
                if (_assignedUserIds.TryGetValue(userData.UserId, out var existing))
                {
                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: User was already connected in another connection, disconnecting");

                    if (_awaitingDisconnectToConnect.Contains(userData.UserId))
                    {
                        connection.Disconnect("Stop trying to connect multiple times at once.");
                        return;
                    }

                    _awaitingDisconnectToConnect.Add(userData.UserId);
                    try
                    {
                        existing.Disconnect("Another connection has been made with your account.");
                        // Have to wait until they're properly off the server to avoid any collisions.

                        _logger.Verbose(
                            $"{connection.RemoteEndPoint}: Awaiting for clean disconnect of previous client");

                        await AwaitDisconnectAsync(existing);

                        _logger.Verbose(
                            $"{connection.RemoteEndPoint}: Previous client disconnected");
                    }
                    finally
                    {
                        _awaitingDisconnectToConnect.Remove(userData.UserId);
                    }
                }

                if (connection.Status == NetConnectionStatus.Disconnecting ||
                    connection.Status == NetConnectionStatus.Disconnected)
                {
                    _logger.Info("{ConnectionEndpoint} ({UserId}/{UserName}) disconnected during handshake",
                        connection.RemoteEndPoint, userData.UserId, userData.UserName);

                    return;
                }

                _logger.Verbose($"{connection.RemoteEndPoint}: Sending MsgLoginSuccess");

                var msg = peer.Peer.CreateMessage();
                var msgResp = new MsgLoginSuccess
                {
                    UserData = userData,
                    Type = type
                };
                if (padSuccessMessage)
                {
                    msg.Write(true);
                    msg.WritePadBits();
                }

                msgResp.WriteToBuffer(msg, _serializer);
                encryption?.Encrypt(msg);
                peer.Peer.SendMessage(msg, connection, NetDeliveryMethod.ReliableOrdered);

                _logger.Info("Approved {ConnectionEndpoint} with username {Username} user ID {userId} into the server",
                    connection.RemoteEndPoint, userData.UserName, userData.UserId);

                // Handshake complete!
                HandleInitialHandshakeComplete(peer, connection, userData, encryption, type);
            }
            catch (ClientDisconnectedException)
            {
                _logger.Info($"Peer {NetUtility.ToHexString(connection.RemoteUniqueIdentifier)} disconnected while handshake was in-progress.");
            }
            catch (Exception e)
            {
                connection.Disconnect("Unknown server error occured during handshake.");
                _logger.Error("Exception during handshake with peer {0}:\n{1}",
                    NetUtility.ToHexString(connection.RemoteUniqueIdentifier), e);
            }
        }

        private async Task<(NetUserId, LoginType)> AssignUserIdAsync(string username)
        {
            if (AssignUserIdCallback == null)
            {
                goto unassigned;
            }

            var assigned = await AssignUserIdCallback(username);
            if (assigned != null)
            {
                return (assigned.Value, LoginType.GuestAssigned);
            }

            unassigned:
            // Just generate a random new GUID.
            var uid = new NetUserId(Guid.NewGuid());
            return (uid, LoginType.Guest);
        }

        private Task AwaitDisconnectAsync(NetConnection connection)
        {
            if (!_awaitingDisconnect.TryGetValue(connection, out var tcs))
            {
                tcs = new TaskCompletionSource<object?>();
                _awaitingDisconnect.Add(connection, tcs);
            }

            return tcs.Task;
        }

        private async void HandleApproval(NetIncomingMessage message)
        {
            DebugTools.Assert(message.SenderConnection != null);
            // TODO: Maybe preemptively refuse connections here in some cases?
            if (message.SenderConnection.Status != NetConnectionStatus.RespondedAwaitingApproval)
            {
                // This can happen if the approval message comes in after the state changes to disconnected.
                // In that case just ignore it.
                return;
            }

            if (HandleApprovalCallback != null)
            {
                var approval = await HandleApprovalCallback(new NetApprovalEventArgs(message.SenderConnection));

                if (!approval.IsApproved)
                {
                    message.SenderConnection.Deny(approval.DenyReason);
                    return;
                }
            }

            message.SenderConnection.Approve();
        }

        // ReSharper disable ClassNeverInstantiated.Local
        private sealed record HasJoinedResponse(bool IsValid, HasJoinedUserData? UserData);
        private sealed record HasJoinedUserData(string UserName, Guid UserId, string? PatronTier);
        // ReSharper restore ClassNeverInstantiated.Local
    }
}
