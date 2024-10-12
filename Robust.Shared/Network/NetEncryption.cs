using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using Lidgren.Network;
using SpaceWizards.Sodium;

namespace Robust.Shared.Network;

internal sealed class NetEncryption
{
    // Use a counter for nonces. The counter is 64-bit, I will be impressed if you ever manage to run it out.
    // 64-bit counter (incl over the wire) is fine, don't need the whole 192-bit.
    // Server starts at 0, client starts at 1, increment by two.
    // This means server and client never use eachother's nonces (one side odd, one side even).
    // Keep in mind, our keys are only valid for one session.
    private ulong _nonce;
    private readonly byte[] _key;

    /// <summary>
    /// How much to offset noonce during reconnect.  Noonce+key combination should not be re-used per
    /// https://doc.libsodium.org/secret-key_cryptography/aead/chacha20-poly1305/ietf_chacha20-poly1305_construction
    ///   "The public nonce npub should never ever be reused with the same key. The recommended way to generate it is to
    ///    use randombytes_buf() for the first message, and increment it for each subsequent message using the same key."
    /// Since key is no longer randomly generated per connection, the noonce must be incremented.  Rather than just
    /// set this to exactly where it left off, I am padding it a bit.  This is just in case there's some messages from
    /// server -> client that the client never received.  This way, I push this much farther into the unused future for
    /// safety.
    /// </summary>
    public const ulong RECONNECT_NOONCE_PADDING = 2000000;

    public NetEncryption(byte[] key, bool isServer)
    {
        if (key.Length != CryptoAeadXChaCha20Poly1305Ietf.KeyBytes)
            throw new ArgumentException("Key is of wrong size!");

        _nonce = isServer ? 0ul : 1ul;
        _key = key;
    }

    public unsafe void Encrypt(NetOutgoingMessage message)
    {
        var nonce = Interlocked.Add(ref _nonce, 2);

        var lengthBytes = message.LengthBytes;
        var encryptedSize = CryptoAeadXChaCha20Poly1305Ietf.AddBytes + lengthBytes + sizeof(ulong);

        var data = message.Data.AsSpan(0, lengthBytes);

        Span<byte> plaintext;
        Span<byte> ciphertext;
        byte[]? returnPool = null;

        if (message.Data.Length >= encryptedSize)
        {
            // Since we have enough space in the existing message data,
            // we copy plaintext to an ArrayPool buffer and write ciphertext into existing message.
            // This avoids an allocation at the cost of an extra copy operation.

            returnPool = ArrayPool<byte>.Shared.Rent(lengthBytes);
            plaintext = returnPool.AsSpan(0, lengthBytes);
            data.CopyTo(plaintext);

            ciphertext = message.Data.AsSpan(0, encryptedSize);
        }
        else
        {
            // Otherwise, an allocation is unavoidable,
            // so we swap the data buffer in the message with a fresh allocation and don't do an extra copy of the data.

            plaintext = data;
            ciphertext = message.Data = new byte[encryptedSize];
        }

        // TODO: this is probably broken for big-endian machines.
        Span<byte> nonceData = stackalloc byte[CryptoAeadXChaCha20Poly1305Ietf.NoncePublicBytes];
        nonceData.Fill(0);
        MemoryMarshal.Write(nonceData, ref nonce);
        MemoryMarshal.Write(ciphertext, ref nonce);

        CryptoAeadXChaCha20Poly1305Ietf.Encrypt(
            // ciphertext
            ciphertext[sizeof(ulong)..],
            out _,
            // plaintext
            plaintext,
            // additional data (unused)
            ReadOnlySpan<byte>.Empty,
            // nonce
            nonceData,
            // key
            _key);

        message.LengthBytes = encryptedSize;

        if (returnPool != null)
            ArrayPool<byte>.Shared.Return(returnPool);
    }

    public unsafe void Decrypt(NetIncomingMessage message)
    {
        var nonce = message.ReadUInt64();
        var cipherText = message.Data.AsSpan(sizeof(ulong), message.LengthBytes - sizeof(ulong));

        var buffer = ArrayPool<byte>.Shared.Rent(cipherText.Length);
        cipherText.CopyTo(buffer);

        // TODO: this is probably broken for big-endian machines.
        Span<byte> nonceData = stackalloc byte[CryptoAeadXChaCha20Poly1305Ietf.NoncePublicBytes];
        nonceData.Fill(0);
        MemoryMarshal.Write(nonceData, ref nonce);

        var result = CryptoAeadXChaCha20Poly1305Ietf.Decrypt(
            // plaintext
            message.Data,
            out var messageLength,
            // ciphertext
            buffer.AsSpan(0, cipherText.Length),
            // additional data (unused)
            ReadOnlySpan<byte>.Empty,
            // nonce
            nonceData,
            // key
            _key);

        message.Position = 0;
        message.LengthBytes = messageLength;

        ArrayPool<byte>.Shared.Return(buffer);

        if (!result)
            throw new SodiumException("Decryption operation failed!");
    }

    public void SetNonce(ulong newValue)
    {
        Interlocked.Exchange(ref _nonce, newValue);
    }

    public ulong GetNonce()
    {
        return Interlocked.Read(ref _nonce);
    }
}
