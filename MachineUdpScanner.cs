using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace OpenCoffee;

/// <summary>
/// UDP scanner to discover coffee machines on the local network.
/// 
/// The decompiled app uses UDP broadcast (UDPManagerBroadcast) and unicast (UDPManagerUnicast)
/// to discover and keep alive connections with machines.
/// 
/// Machines respond with a ~110+ byte packet containing:
///   - Bytes 0-1:   Total data length
///   - Bytes 2-3:   Keyword (must contain 0x05F3 = 1523 in lower 12 bits)
///   - Bytes 4-19:  Frog version string (16 bytes, trimmed)
///   - Bytes 20-51: Custom name (32 bytes, trimmed)
///   - Bytes 52-67: Coffee machine version string (16 bytes, trimmed)
///   - Bytes 68-77: Machine info (article#, machine#, serial#, production dates)
///   - Bytes 103-108: Hardware (MAC) address
///   - Byte 109:    Connection state flags:
///                     bit 0: 0=progress, 1=status
///                     bit 4: 1=requires PIN
///                     bit 7: 1=ready, 0=busy
///   - Bytes 110+:  Current status data
/// </summary>
public class MachineUdpScanner
{
    public const int UdpPort = 51515;

    /// <summary>
    /// Scan for coffee machines by sending a UDP broadcast and listening for responses.
    /// </summary>
    /// <param name="timeoutMs">How long to listen for responses (milliseconds)</param>
    public static async Task ScanAsync(int timeoutMs = 3000)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        // The app uses port 51515 for UDP as well
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, UdpPort);

        // Send an empty broadcast to trigger responses from machines
        byte[] probe = Encoding.UTF8.GetBytes("");
        await udp.SendAsync(probe, probe.Length, broadcastEndpoint);

        // Console.WriteLine($"[JURA UDP] Broadcast sent on port {UdpPort}, listening for {timeoutMs}ms...");

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (udp.Available > 0)
            {
                var result = await udp.ReceiveAsync();
                ParseDiscoveryPacket(result.Buffer, result.RemoteEndPoint);
            }
            else
            {
                await Task.Delay(100);
            }
        }
    }

    private static void ParseDiscoveryPacket(byte[] data, IPEndPoint sender)
    {
        if (data.Length <= 110)
        {
            // Console.WriteLine($"[JURA UDP] Packet from {sender} too short ({data.Length} bytes)");
            return;
        }

        try
        {
            // Parse key fields from the frog broadcast data
            int totalLength = (data[0] << 8) | data[1];
            int keyword = ((data[2] << 8) | data[3]) & 0x0FFF;

            if (keyword != 1523)
            {
                // Console.WriteLine($"[JURA UDP] Invalid keyword {keyword} from {sender}");
                return;
            }

            string frogVersion = Encoding.UTF8.GetString(data, 4, 16).Trim();
            string customName = Encoding.UTF8.GetString(data, 20, 32).Trim();
            string machineVersion = Encoding.UTF8.GetString(data, 52, 16).Trim();

            int articleNumber = (data[68] << 8) | data[69];
            int machineNumber = (data[70] << 8) | data[71];
            int serialNumber = (data[72] << 8) | data[73];

            byte[] mac = new byte[6];
            Array.Copy(data, 103, mac, 0, 6);
            string macAddress = BitConverter.ToString(mac).Replace("-", ":");

            byte connectionFlags = data[109];
            bool requiresPin = ((connectionFlags >> 4) & 1) == 1;
            bool isReady = ((connectionFlags >> 7) & 1) == 1;

            Console.WriteLine($"[Coffee UDP] Found machine at {sender.Address}:");
            Console.WriteLine($"  Name:         {customName}");
            Console.WriteLine($"  Frog Version: {frogVersion}");
            Console.WriteLine($"  Machine Ver:  {machineVersion}");
            Console.WriteLine($"  Article#:     {articleNumber}");
            Console.WriteLine($"  Machine#:     {machineNumber}");
            Console.WriteLine($"  Serial#:      {serialNumber}");
            Console.WriteLine($"  MAC:          {macAddress}");
            Console.WriteLine($"  Requires PIN: {requiresPin}");
            Console.WriteLine($"  Ready:        {isReady}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Coffee UDP] Error parsing packet from {sender}: {ex.Message}");
        }
    }
}
