using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCoffee;

/// <summary>
/// C# replication of the coffee machine's WiFi TCP protocol for communicating 
/// with coffee machines via the WiFi Connect (Smart Connect) module.
/// 
/// Protocol overview (from decompiled APK analysis):
/// ─────────────────────────────────────────────────
/// 
/// 1. DISCOVERY: The machine broadcasts UDP packets on the local network.
///    The app discovers machines via UDP broadcast/unicast scanning.
///
/// 2. TCP CONNECTION: Once a machine is found, the app connects via TCP to
///    port 51515 on the machine's IP address.
///
/// 3. ENCRYPTION: All TCP data is encrypted with a custom symmetric cipher.
///    - Sending: data → encrypt(data, random_key) → prepend 0x2A (*) → append 0x0D 0x0A (CRLF)
///    - Receiving: strip 0x2A → decrypt(payload) → plaintext string
///    See <see cref="CryptoUtil"/> for the cipher implementation.
///
/// 4. CONNECTION SETUP: After TCP connect, the first command sent is:
///      @HP:{pin},{deviceNameHex},{hash}
///    Where:
///    - pin: 4-6 digit PIN (empty string if machine has no PIN)
///    - deviceNameHex: the connecting device name encoded as hex (each char → 2-char uppercase hex)
///    - hash: a session hash returned by the machine on first successful connect (empty string initially)
///    
///    Response codes:
///    - @hp4        → Connection accepted (no hash change)
///    - @hp4:{hash} → Connection accepted, store the new hash for future use
///    - @hp5        → Wrong PIN  
///    - @hp5:00     → Wrong PIN
///    - @hp5:01     → Wrong hash (hash expired/invalid)
///    - @hp5:02     → Connection aborted
///
/// 5. LOCKING: Before sending settings commands, the machine is locked:
///    - Lock:   @TS:01  → response: @ts
///    - Unlock: @TS:00  → response: @ts
///
/// 6. COMMANDS: Various @ commands for reading status, products, settings, etc.
///    - @TF       → Read machine status  (response: @TF:hexdata)
///    - @TV:...   → Progress updates       (response: @TV:hexdata)  
///    - @HW:01,{pin} → Set new PIN code
///    
/// All commands end with \r\n (CRLF).
/// </summary>
public class TcpClient : IDisposable
{
    public const int DefaultPort = 51515;
    public const int MaxSegmentSize = 1500;
    private System.Net.Sockets.TcpClient _tcpClient;
    private NetworkStream _networkStream;
    private CancellationTokenSource _cts;
    private readonly string _host;
    private readonly int _port;
    private string _pin;
    private string _deviceName;
    private string _hash;

    /// <summary>Fires when a status message (@TF:...) is received.</summary>
    public event Action<string> OnStatusReceived;

    /// <summary>Fires when a progress message (@TV:...) is received.</summary>
    public event Action<string> OnProgressReceived;

    /// <summary>Fires when any message is received (for debugging).</summary>
    public event Action<string> OnMessageReceived;

    /// <summary>Fires when the connection is lost.</summary>
    public event Action OnDisconnected;

    /// <summary>
    /// Create a new Coffee TCP client.
    /// </summary>
    /// <param name="host">IP address of the coffee machine (Smart Connect module)</param>
    /// <param name="port">TCP port (default 51515)</param>
    /// <param name="deviceName">Your device/app name (will be hex-encoded in the handshake)</param>
    /// <param name="pin">PIN code for the machine (empty string if no PIN is set)</param>
    /// <param name="hash">Session hash from a previous connection (empty string if first connection)</param>
    public TcpClient(string host, int port = DefaultPort, string deviceName = "CSharpClient",
                          string pin = "", string hash = "")
    {
        _host = host;
        _port = port;
        _deviceName = deviceName;
        _pin = pin;
        _hash = hash;
    }

    /// <summary>
    /// The session hash returned by the machine after successful connection setup.
    /// Store this and provide it in future connections to avoid re-entering the PIN.
    /// </summary>
    public string SessionHash => _hash;

    /// <summary>
    /// Connect to the coffee machine and perform the connection setup handshake.
    /// </summary>
    /// <returns>True if connection and authentication succeeded.</returns>
    public async Task<ConnectionSetupResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _tcpClient = new System.Net.Sockets.TcpClient();
        _tcpClient.ReceiveBufferSize = MaxSegmentSize;
        _tcpClient.SendBufferSize = MaxSegmentSize;

        await _tcpClient.ConnectAsync(_host, _port);
        _networkStream = _tcpClient.GetStream();

        Console.WriteLine($"[Coffee] Connected to {_host}:{_port}");

        // Start the background receiver BEFORE the handshake so we can read responses
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);

        // Perform the connection setup handshake
        var result = await ConnectionSetupAsync();

        return result;
    }

    /// <summary>
    /// Send a raw command string to the machine (will be encrypted and framed).
    /// The command should NOT include the trailing \r\n - it will be added.
    /// </summary>
    public async Task SendCommandAsync(string command)
    {
        if (_networkStream == null || !_tcpClient.Connected)
            throw new InvalidOperationException("Not connected");

        // The WifiCommand class appends \r\n to the command string before encoding
        string fullCommand = command + "\r\n";
        byte[] plainBytes = Encoding.UTF8.GetBytes(fullCommand);

        int key = CryptoUtil.GenerateKey();
        byte[] encrypted = CryptoUtil.Encrypt(plainBytes, key);

        // Wire format: 0x2A (star) + encrypted_payload + 0x0D 0x0A (CRLF)
        using var ms = new MemoryStream();
        ms.WriteByte(0x2A); // '*'
        ms.Write(encrypted, 0, encrypted.Length);
        ms.WriteByte(0x0D); // CR
        ms.WriteByte(0x0A); // LF
        byte[] frame = ms.ToArray();

        // Console.WriteLine($"[JURA] SEND: {command}");
        // Console.WriteLine($"[JURA] SEND hex ({frame.Length} bytes): {BitConverter.ToString(frame).Replace("-", " ")}");
        await _networkStream.WriteAsync(frame, 0, frame.Length);
        await _networkStream.FlushAsync();
    }

    /// <summary>
    /// Send a command and wait for a matching response.
    /// </summary>
    /// <param name="command">Command without \r\n</param>
    /// <param name="responsePattern">Regex pattern the response must match</param>
    /// <param name="timeoutSeconds">How long to wait for a response</param>
    /// <returns>The decrypted response string, or null on timeout</returns>
    public async Task<string?> SendCommandWithResponseAsync(string command, string responsePattern,
                                                             int timeoutSeconds = 5)
    {
        var tcs = new TaskCompletionSource<string>();
        var regex = new Regex(responsePattern);

        void Handler(string msg)
        {
            if (regex.IsMatch(msg))
            {
                tcs.TrySetResult(msg);
            }
        }

        OnMessageReceived += Handler;
        try
        {
            await SendCommandAsync(command);

            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))
            );

            return completedTask == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            OnMessageReceived -= Handler;
        }
    }

    /// <summary>
    /// Read the machine status.
    /// Sends @TF and expects a response matching @T(F|V):.*
    /// </summary>
    public async Task<string?> ReadStatusAsync()
    {
        return await SendCommandWithResponseAsync("@TF", @"@T[FV]:.*");
    }

    /// <summary>
    /// Lock the machine for exclusive access (required before writing settings).
    /// Sends @TS:01
    /// </summary>
    public async Task<string?> LockMachineAsync()
    {
        return await SendCommandWithResponseAsync("@TS:01", @"@ts.*");
    }

    /// <summary>
    /// Unlock the machine.
    /// Sends @TS:00
    /// </summary>
    public async Task<string?> UnlockMachineAsync()
    {
        return await SendCommandWithResponseAsync("@TS:00", @"@ts.*");
    }

    /// <summary>
    /// Set a new PIN code on the machine.
    /// </summary>
    public async Task<string?> SetPinAsync(string newPin)
    {
        return await SendCommandWithResponseAsync($"@HW:01,{newPin}", @"@hw:01.*");
    }

    /// <summary>
    /// Read maintenance status percentages (filter life, cleaning status, etc.).
    /// Sends @TG:C0 and expects @tg:C0{hex} response.
    /// </summary>
    public async Task<MaintenanceStatus?> ReadMaintenanceStatusAsync()
    {
        string? response = await SendCommandWithResponseAsync("@TG:C0", @"@tg:C0.*");
        if (response == null) return null;
        return MaintenanceStatus.Parse(response.TrimEnd());
    }

    /// <summary>
    /// Read maintenance counters (how many times each maintenance action was performed).
    /// Sends @TG:43 and expects @tg:43{hex} response.
    /// </summary>
    public async Task<MaintenanceCounters?> ReadMaintenanceCountersAsync()
    {
        string? response = await SendCommandWithResponseAsync("@TG:43", @"@tg:43.*");
        if (response == null) return null;
        return MaintenanceCounters.Parse(response.TrimEnd());
    }

    /// <summary>
    /// Read product counter statistics (how many cups of each type were made).
    /// Reads all 16 pages of @TR:32 data (pages 0x00-0x0F), each containing 4 products.
    /// This is the same multi-page protocol the J.O.E. app uses.
    /// </summary>
    public async Task<ProductCounters> ReadProductCountersAsync()
    {
        var pageData = new Dictionary<int, string>();

        for (int page = 0; page <= 15; page++)
        {
            string pageHex = page.ToString("X2");
            string command = $"@TR:32,{pageHex}";
            // Response pattern: @tr:32,{pageHex},{data} OR @tr:00 (empty/error)
            string pattern = $@"((@tr:32,{pageHex},.*)|(@tr:00))";

            string? response = await SendCommandWithResponseAsync(command, pattern, timeoutSeconds: 5);
            if (response == null || response.TrimEnd().StartsWith("@tr:00"))
            {
                // No data for this page, use default padding
                pageData[page] = "0000000000000000";
                continue;
            }

            // Response: "@tr:32,XX,{pageCounter:3hexchars}{productData:16hexchars}\r\n"
            string trimmed = response.TrimEnd('\r', '\n');
            
            // Find the data after "@tr:32,XX,"
            string prefix = $"@tr:32,{pageHex},";
            int idx = trimmed.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                // Try lowercase
                prefix = $"@tr:32,{page:x2},";
                idx = trimmed.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            }

            if (idx >= 0)
            {
                // pagePayload is everything after "@tr:32,XX," — this is the raw product data
                // (4 products × 2 bytes × 2 hex chars = 16 hex chars)
                string productData = trimmed.Substring(idx + prefix.Length);
                // Ensure exactly 16 hex chars
                if (productData.Length < 16)
                    productData = productData.PadRight(16, '0');
                else if (productData.Length > 16)
                    productData = productData.Substring(0, 16);
                pageData[page] = productData;
            }
            else
            {
                pageData[page] = "0000000000000000";
            }
        }

        return ProductCounters.ParseFromPages(pageData);
    }

    /// <summary>
    /// Read the parsed machine status (alerts, coffee ready flag, etc.).
    /// </summary>
    public async Task<MachineStatus?> ReadMachineStatusAsync()
    {
        string? response = await ReadStatusAsync();
        if (response == null) return null;
        string trimmed = response.TrimEnd('\r', '\n');
        if (trimmed.StartsWith("@TF:"))
        {
            return MachineStatus.FromHex(trimmed.Substring(4));
        }
        return null;
    }

    /// <summary>
    /// Disconnect and clean up.
    /// </summary>
    public void Disconnect()
    {
        try
        {
            _cts?.Cancel();
            _networkStream?.Close();
            _tcpClient?.Close();
        }
        catch { }

        Console.WriteLine("[Coffee] Disconnected");
        OnDisconnected?.Invoke();
    }

    public void Dispose() => Disconnect();

    // ──────────────────────────────────────────────
    // Private implementation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Perform the @HP connection setup handshake.
    /// 
    /// Format: @HP:{pin},{deviceNameHex},{hash}
    /// 
    /// The device name is encoded as uppercase hex (each ASCII char → 2 hex chars).
    /// The PIN is sent as plaintext digits.
    /// The hash is a string returned by the machine on first connection.
    /// </summary>
    private async Task<ConnectionSetupResult> ConnectionSetupAsync()
    {
        string deviceNameHex = StringToHex(_deviceName);
        string command = $"@HP:{_pin},{deviceNameHex},{_hash}";

        string? response = await SendCommandWithResponseAsync(command, @"((@hp4)|(@hp5))(:.*)?\r?\n?", timeoutSeconds: 30);

        if (response == null)
        {
            Console.WriteLine("[Coffee] Connection setup timed out");
            return ConnectionSetupResult.Timeout;
        }

        Console.WriteLine($"[Coffee] Connection setup response: {response}");

        // Parse the response according to ConnectionSetupParser
        if (Regex.IsMatch(response, @"^@hp5(:00)?\r?\n?$"))
        {
            // Wrong PIN
            _hash = "";  // Clear hash (pin is invalidated)
            return ConnectionSetupResult.WrongPin;
        }
        if (Regex.IsMatch(response, @"^@hp5:01\r?\n?$"))
        {
            // Wrong hash - need to re-authenticate
            _hash = "";
            return ConnectionSetupResult.WrongHash;
        }
        if (Regex.IsMatch(response, @"^@hp5:02\r?\n?$"))
        {
            // Aborted
            _hash = "";
            return ConnectionSetupResult.Aborted;
        }
        if (Regex.IsMatch(response, @"^@hp4\r?\n?$"))
        {
            // Correct, no new hash
            return ConnectionSetupResult.Correct;
        }
        if (Regex.IsMatch(response, @"^@hp4:.*"))
        {
            // Correct with new hash
            string newHash = response.Substring(5).TrimEnd('\r', '\n');
            _hash = newHash;
            Console.WriteLine($"[Coffee] New session hash: {_hash}");
            return ConnectionSetupResult.Correct;
        }

        return ConnectionSetupResult.Unknown;
    }

    /// <summary>
    /// Background loop that reads encrypted frames from the TCP stream.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[MaxSegmentSize];

        try
        {
            while (!ct.IsCancellationRequested && _tcpClient.Connected)
            {
                int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead <= 0)
                {
                    Console.WriteLine("[Coffee] Connection closed by machine");
                    break;
                }

                try
                {
                    // Log raw bytes for debugging
                    byte[] rawSlice = new byte[bytesRead];
                    Array.Copy(buffer, rawSlice, bytesRead);
                    // Console.WriteLine($"[JURA] RECV raw ({bytesRead} bytes): {BitConverter.ToString(rawSlice).Replace("-", " ")}");

                    // Decrypt the received data
                    byte[] decrypted = CryptoUtil.Decrypt(buffer, bytesRead);
                    string message = Encoding.UTF8.GetString(decrypted);

                    // Console.WriteLine($"[JURA] RECV: {message.TrimEnd()}");
                    OnMessageReceived?.Invoke(message);

                    // Route to specific handlers
                    if (message.StartsWith("@TF:"))
                        OnStatusReceived?.Invoke(message);
                    else if (message.StartsWith("@TV:"))
                        OnProgressReceived?.Invoke(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Coffee] Error decrypting: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[Coffee] Receive error: {ex.Message}");
        }

        OnDisconnected?.Invoke();
    }

    /// <summary>
    /// Convert a string to uppercase hex representation (each char → 2-char hex).
    /// This matches ExtensionsKt.toHexString(String) from the decompiled app.
    /// </summary>
    private static string StringToHex(string input)
    {
        var sb = new StringBuilder(input.Length * 2);
        foreach (char c in input)
        {
            sb.Append(((int)c).ToString("X2"));
        }
        return sb.ToString();
    }
}

/// <summary>
/// Result of the @HP connection setup handshake.
/// Maps to ConnectionSetupState enum from the decompiled app.
/// </summary>
public enum ConnectionSetupResult
{
    /// <summary>Authentication successful, connection established.</summary>
    Correct,
    /// <summary>PIN was wrong.</summary>
    WrongPin,
    /// <summary>Session hash was wrong/expired. Try with empty hash or re-enter PIN.</summary>
    WrongHash,
    /// <summary>Connection was aborted by the machine.</summary>
    Aborted,
    /// <summary>Connection setup timed out.</summary>
    Timeout,
    /// <summary>Unknown response.</summary>
    Unknown
}
