using System;
using System.Collections.Generic;

namespace OpenCoffee;

/// <summary>
/// Replicates the coffee machine WiFi encryption/decryption protocol.
/// 
/// The protocol uses a custom symmetric cipher based on two substitution discs
/// and a per-message random key byte. Each byte of plaintext is split into two nibbles (half-bytes),
/// each nibble is passed through an encode/decode function that uses the disc tables, the key,
/// and a running turn counter. The result is reassembled into bytes.
/// 
/// Special characters (0x00, 0x0A, 0x0D, 0x26, 0x1B) are escaped with ESC (0x1B) ^ 0x80.
/// 
/// Wire format for sending:  0x2A (star) | encrypted_payload | 0x0D 0x0A (CRLF)
/// Wire format for receiving: data starts after the first 0x2A byte; encrypted payload ends at 0x0D.
/// </summary>
public static class CryptoUtil
{
    // Substitution disc tables (from decompiled WifiCryptoUtil)
    private static readonly byte[] DiscOne = { 1, 0, 3, 2, 15, 14, 8, 10, 6, 13, 7, 12, 11, 9, 5, 4 };
    private static readonly byte[] DiscTwo = { 9, 12, 6, 11, 10, 15, 2, 14, 13, 0, 4, 3, 1, 8, 7, 5 };

    // Characters that must be escaped in the wire protocol
    private static readonly HashSet<int> EscapedChars = new HashSet<int> { 0, 10, 13, 38, 27 };

    /// <summary>
    /// Generate a random key byte for encryption.
    /// The low nibble must not be 14 (0x0E) or 15 (0x0F).
    /// </summary>
    public static int GenerateKey()
    {
        var random = new Random((int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & int.MaxValue));
        while (true)
        {
            int key = random.Next(256);
            int lowNibble = key & 0x0F;
            if (lowNibble != 15 && lowNibble != 14)
                return key;
        }
    }

    /// <summary>
    /// Encrypt plaintext bytes for sending to the coffee machine.
    /// Returns the encrypted payload (without the leading 0x2A or trailing CRLF).
    /// </summary>
    public static byte[] Encrypt(byte[] sourceBytes, int key)
    {
        // Worst case: each byte can become 2 bytes (ESC + encoded), plus the key byte
        byte[] buffer = new byte[sourceBytes.Length * 2 + 2];

        int ucBaseDiscOne = NormalizeTo255(NormalizeTo255(key >> 4));
        int ucBaseDiscTwo = NormalizeTo255(key);

        byte keyByte = (byte)key;
        int writePos = 0;

        // Write the key byte (possibly escaped)
        if (NeedsESC(keyByte))
        {
            buffer[writePos++] = 0x1B; // ESC
            buffer[writePos++] = (byte)(key ^ 0x80);
        }
        else
        {
            buffer[writePos++] = keyByte;
        }

        int turn = 0;
        for (int i = 0; i < sourceBytes.Length; i++)
        {
            byte b = sourceBytes[i];
            int highNibble = NormalizeTo255(b >> 4);
            int lowNibble = NormalizeTo255(b & 0x0F);

            int encodedHigh = EncodeDecodeHalfByte(highNibble, turn, ucBaseDiscOne, ucBaseDiscTwo);
            int encodedLow = EncodeDecodeHalfByte(lowNibble, turn + 1, ucBaseDiscOne, ucBaseDiscTwo);
            turn += 2;

            int encodedByte = NormalizeTo255(encodedLow | NormalizeTo255(encodedHigh << 4));

            if (NeedsESC((byte)encodedByte))
            {
                buffer[writePos++] = 0x1B; // ESC
                encodedByte ^= 0x80;
            }
            buffer[writePos++] = (byte)NormalizeTo255(encodedByte);
        }

        byte[] result = new byte[writePos];
        Array.Copy(buffer, result, writePos);
        return result;
    }

    /// <summary>
    /// Decrypt data received from the coffee machine.
    /// Input should be the raw bytes from the TCP stream (starting after the 0x2A star marker,
    /// but including the key byte up to and including the 0x0D byte).
    /// </summary>
    public static byte[] Decrypt(byte[] sourceBytes, int length)
    {
        byte[] output = new byte[length];

        int readPos;
        // The key byte may be ESC-encoded
        if (IsESC(sourceBytes[1]))
        {
            readPos = 2;
            sourceBytes[2] = (byte)(sourceBytes[2] ^ 0x80);
        }
        else
        {
            readPos = 1;
        }

        byte keyByte = sourceBytes[readPos];
        int ucBaseDiscOne = (byte)((keyByte >> 4) & 0x0F);
        int ucBaseDiscTwo = (byte)(keyByte & 0x0F);

        readPos++;
        int writePos = 0;
        byte turn = 0;

        while (!IsCR(sourceBytes[readPos]))
        {
            byte b = sourceBytes[readPos];
            if (IsESC(b))
            {
                readPos++;
                b = (byte)(sourceBytes[readPos] ^ 0x80);
            }

            byte highNibble = GetLeftHalf(b);
            byte lowNibble = GetRightHalf(b);

            byte decodedHigh = (byte)EncodeDecodeHalfByte(highNibble, turn, ucBaseDiscOne, ucBaseDiscTwo);
            byte decodedLow = (byte)EncodeDecodeHalfByte(lowNibble, (byte)(turn + 1), ucBaseDiscOne, ucBaseDiscTwo);
            turn = (byte)(turn + 2);

            output[writePos] = MergeHalfBytes(decodedHigh, decodedLow);
            readPos++;
            writePos++;
        }

        byte[] result = new byte[writePos];
        Array.Copy(output, result, writePos);
        return result;
    }

    /// <summary>
    /// Core encoding/decoding function for a single nibble (half-byte).
    /// This is the heart of the JURA cipher - it uses two substitution disc tables
    /// combined with running counters.
    /// </summary>
    private static int EncodeDecodeHalfByte(int ucData, int ucTurn, int ucBaseDiscOne, int ucBaseDiscTwo)
    {
        int step1 = NormalizeTo255((ucData + ucTurn) + ucBaseDiscOne) % 16;

        int innerDisc1 = NormalizeTo255(step1);
        int disc1Val = DiscOne[innerDisc1];

        int turnShift = ucTurn >> 4;

        int disc2Index = NormalizeTo255((((disc1Val + ucBaseDiscTwo) + NormalizeTo255(turnShift)) - ucTurn) - ucBaseDiscOne) % 16;
        int disc2Val = DiscTwo[disc2Index];

        int disc1Index2 = NormalizeTo255((((disc2Val + ucBaseDiscOne) + ucTurn) - ucBaseDiscTwo) - NormalizeTo255(turnShift)) % 16;
        int disc1Val2 = DiscOne[disc1Index2];

        return NormalizeTo255(NormalizeTo255((disc1Val2 - ucTurn) - ucBaseDiscOne) % 16);
    }

    private static int NormalizeTo255(int value)
    {
        // Equivalent to modular arithmetic in 0-255 range
        while (value > 255) value -= 256;
        while (value < 0) value += 256;
        return value;
    }

    private static bool NeedsESC(byte b) => EscapedChars.Contains(b);
    private static bool IsESC(byte b) => b == 0x1B;
    private static bool IsCR(byte b) => b == 0x0D;
    private static byte GetLeftHalf(byte b) => (byte)((b >> 4) & 0x0F);
    private static byte GetRightHalf(byte b) => (byte)(b & 0x0F);
    private static byte MergeHalfBytes(byte left, byte right) => (byte)((left << 4) | right);
}
