using System.Collections.Immutable;
using Lidgren.Network;
using Robust.Shared.Serialization;

#nullable disable

namespace Robust.Shared.Network.Messages.Handshake
{
    internal sealed class MsgLoginStart : NetMessage
    {
        // **NOTE**: This is a special message sent during the client<->server handshake.
        // It doesn't actually get sent normally and as such doesn't have the "normal" boilerplate.
        // It's basically just a sane way to encapsulate the message write/read logic.
        public override string MsgName => string.Empty;

        public override MsgGroups MsgGroup => MsgGroups.Core;

        /// <summary>
        /// This is the username the player prefers -- however, the server may end up assigning a
        /// derivative based on it.
        /// </summary>
        public string PreferredUserName;

        public ImmutableArray<byte> HWIdLegacy;
        public bool CanAuth;
        public bool NeedServerPublicKey;
        public bool Encrypt;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            PreferredUserName = buffer.ReadString();
            CanAuth = buffer.ReadBoolean();
            NeedServerPublicKey = buffer.ReadBoolean();
            Encrypt = buffer.ReadBoolean();

            // Including legacy HW id here since guests don't send
            // auth packets.  Technically means legacy HWID gets sent twice
            // for authed users currently...
            var legacyHwIdlength = buffer.ReadByte();
            if (legacyHwIdlength > 0)
            {
                HWIdLegacy = ImmutableArray.Create(buffer.ReadBytes(legacyHwIdlength));
            }
            else
                HWIdLegacy = ImmutableArray<byte>.Empty;
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.Write(PreferredUserName);
            buffer.Write(CanAuth);
            buffer.Write(NeedServerPublicKey);
            buffer.Write(Encrypt);

            if (HWIdLegacy != null && HWIdLegacy.Length > 0)
            {
                buffer.Write((byte) HWIdLegacy.Length);
                buffer.Write(HWIdLegacy.AsSpan());
            } else {
                buffer.Write((byte) 0);
            }
        }
    }
}
