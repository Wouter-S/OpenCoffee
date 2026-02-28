using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OpenCoffee;

/// <summary>
/// Parsed machine status from @TF response.
/// The response data is a hex string representing a byte array where each bit is a status flag.
/// Bit positions are indexed from MSB (bit 0 = MSB of byte 0).
/// </summary>
public class MachineStatus
{
    // Known bit positions from the decompiled app (Status.java)
    public const int BIT_COFFEE_READY = 13;
    public const int BIT_POWDER_PRODUCT = 28;
    public const int BIT_DECALC_ALERT = 33;
    public const int BIT_CAPPUCLEAN_ALERT = 41;
    public const int BIT_COFFEE_EYE = 56;
    public const int BIT_INCASSO_CONNECTED = 145;
    public const int BIT_T_PROTOCOL_INITIALIZED = 150;

    // Alert bit ranges (from the ignoredAlerts and readWriteNotAllowedArray in Status.java)
    // Blocking alerts: bits 0-6, 8-9, 11-12, 17-31, 40, 47-48
    private static readonly int[] BlockingAlertBits = {
        0, 1, 2, 3, 4, 5, 6, 8, 9, 11, 12, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 40, 47, 48
    };

    private readonly byte[] _data;

    public MachineStatus(byte[] data)
    {
        _data = data;
    }

    /// <summary>Parse from hex string (the part after "@TF:").</summary>
    public static MachineStatus FromHex(string hex)
    {
        if (hex.Length % 2 == 1) hex += "0";
        byte[] data = new byte[hex.Length / 2];
        for (int i = 0; i < data.Length; i++)
            data[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber);
        return new MachineStatus(data);
    }

    /// <summary>Read a single bit from the status data. MSB-first indexing.</summary>
    public bool GetBit(int index)
    {
        if (index >= _data.Length * 8) return false;
        int byteIdx = index / 8;
        int bitIdx = 7 - (index % 8);
        return (_data[byteIdx] & (1 << bitIdx)) != 0;
    }

    public bool CoffeeReady => GetBit(BIT_COFFEE_READY);
    public bool TProtocolInitialized => GetBit(BIT_T_PROTOCOL_INITIALIZED);
    public bool PowderProduct => GetBit(BIT_POWDER_PRODUCT);
    public bool DecalcAlert => GetBit(BIT_DECALC_ALERT);
    public bool CappuCleanAlert => GetBit(BIT_CAPPUCLEAN_ALERT);
    public bool IncassoConnected => GetBit(BIT_INCASSO_CONNECTED);
    public bool IsInteractionAllowed => CoffeeReady && TProtocolInitialized;

    /// <summary>Returns all active blocking alert bit indices.</summary>
    public List<int> ActiveBlockingAlerts =>
        BlockingAlertBits.Where(b => GetBit(b)).ToList();

    public bool HasBlockingAlerts => ActiveBlockingAlerts.Count > 0;

    public string RawHex => BitConverter.ToString(_data).Replace("-", "");

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Coffee Ready:        {CoffeeReady}");
        sb.AppendLine($"  T-Protocol Init:     {TProtocolInitialized}");
        sb.AppendLine($"  Interaction Allowed: {IsInteractionAllowed}");
        sb.AppendLine($"  Powder Product:      {PowderProduct}");
        sb.AppendLine($"  Decalc Alert:        {DecalcAlert}");
        sb.AppendLine($"  CappuClean Alert:    {CappuCleanAlert}");
        sb.AppendLine($"  Blocking Alerts:     {(HasBlockingAlerts ? string.Join(", ", ActiveBlockingAlerts) : "None")}");
        return sb.ToString();
    }
}
