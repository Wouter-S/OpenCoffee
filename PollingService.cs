using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCoffee;

public class PollingService : BackgroundService
{
    private readonly Settings _settings;
    private readonly ILogger<PollingService> _logger;
    private readonly MqttPublisher _mqtt;

    public PollingService(
        IOptions<Settings> settings,
        ILogger<PollingService> logger,
        MqttPublisher mqtt)
    {
        _settings = settings.Value;
        _logger = logger;
        _mqtt = mqtt;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Coffee polling service started. Machine={Ip}, Interval={Min} min",
            _settings.MachineIp, _settings.PollIntervalMinutes);

        // Initial delay to let the host finish startup logging
        await Task.Delay(500, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollMachineAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during poll cycle");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(_settings.PollIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("Coffee polling service stopped");
    }

    private async Task PollMachineAsync(CancellationToken ct)
    {
        string hash = HashStore.Load();
        _logger.LogDebug("Loaded hash: {Hash}",
            string.IsNullOrEmpty(hash) ? "(none)" : hash[..8] + "...");

        using var client = new TcpClient(
            host: _settings.MachineIp,
            deviceName: _settings.DeviceName,
            pin: _settings.Pin,
            hash: hash);

        var result = await client.ConnectAsync(ct);
        _logger.LogInformation("Connection result: {Result}", result);

        if (result != ConnectionSetupResult.Correct)
        {
            _logger.LogWarning("Connection failed ({Result}). Will retry next cycle.", result);
            return;
        }

        // Persist hash if it changed
        if (!string.IsNullOrEmpty(client.SessionHash) && client.SessionHash != hash)
        {
            HashStore.Save(client.SessionHash);
            _logger.LogInformation("Saved new session hash to hash.json");
        }

        // ── Read all data ──
        var machineStatus = await client.ReadMachineStatusAsync();
        var maintStatus = await client.ReadMaintenanceStatusAsync();
        var maintCounters = await client.ReadMaintenanceCountersAsync();
        var productCounters = await client.ReadProductCountersAsync();

        // ── Build snapshot ──
        var snapshot = new MachineSnapshot
        {
            CoffeeReady = machineStatus?.CoffeeReady ?? false,
            HasAlerts = machineStatus?.HasBlockingAlerts ?? false,
            ActiveAlerts = machineStatus?.HasBlockingAlerts == true
                ? machineStatus.ActiveBlockingAlerts : null,
            Maintenance = maintStatus != null
                ? MachineSnapshot.MaintenanceStatusDto.From(maintStatus) : null,
            MaintenanceCounts = maintCounters != null
                ? MachineSnapshot.MaintenanceCountersDto.From(maintCounters) : null,
            Products = productCounters != null
                ? MachineSnapshot.ProductsDto.From(productCounters) : null,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(snapshot,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        Console.WriteLine(json);

        // Publish to MQTT
        try
        {
            await _mqtt.PublishAsync(json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish to MQTT");
        }

        _logger.LogInformation("Poll complete \u2014 CoffeeReady={Ready}, Total={Total}",
            snapshot.CoffeeReady, snapshot.Products?.TotalProducts ?? 0);

        client.Disconnect();
    }
}
