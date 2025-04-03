using System;
using Lidgren.Network;
using Robust.Shared.Serialization;

#nullable disable

namespace Robust.Shared.Network.Messages.Handshake
{
    internal sealed class MsgEncryptionResponse : NetMessage
    {
        public override string MsgName => string.Empty;

        public override MsgGroups MsgGroup => MsgGroups.Core;

        public byte[] SealedData;
        public string UserJWT;
        public string UserPublicKey;
        public ulong StartingNonce;
        public byte[] LegacyHwid;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var keyLength = buffer.ReadVariableInt32();
            SealedData = buffer.ReadBytes(keyLength);

            UserJWT = buffer.ReadString();
            UserPublicKey = buffer.ReadString();
            StartingNonce = buffer.ReadUInt64();
            var legacyHwidLength = buffer.ReadVariableInt32();
            LegacyHwid = buffer.ReadBytes(legacyHwidLength);
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.WriteVariableInt32(SealedData.Length);
            buffer.Write(SealedData);

            buffer.Write(UserJWT);
            buffer.Write(UserPublicKey);
            buffer.Write(StartingNonce);
            buffer.WriteVariableInt32(LegacyHwid.Length);
            buffer.Write(LegacyHwid);
        }
    }
}
