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

        private const int MaxFieldLength = 1024;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var keyLength = buffer.ReadVariableInt32();
            if (keyLength is < 0 or > (int)NetMessageExt.MaxSizeOfEncryption)
                throw new InvalidOperationException($"Sealed data length {keyLength} out of range.");

            SealedData = buffer.ReadBytes(keyLength);

            UserJWT = buffer.ReadString();
            UserPublicKey = buffer.ReadString();
            StartingNonce = buffer.ReadUInt64();
            var legacyHwidLength = buffer.ReadVariableInt32();
            if (legacyHwidLength is < 0 or > (int)NetMessageExt.MaxSizeOfHwid)
                throw new InvalidOperationException($"Legacy HWID length {legacyHwidLength} out of range.");

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
