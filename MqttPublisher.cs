using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace OpenCoffee;

/// <summary>
/// Manages a persistent MQTT connection and exposes a simple publish method.
/// Registered as a singleton so the connection is shared across the app lifetime.
/// </summary>
public sealed class MqttPublisher : IAsyncDisposable
{
    private readonly Settings _settings;
    private readonly ILogger<MqttPublisher> _logger;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _options;
    private bool _disposed;

    public MqttPublisher(IOptions<Settings> settings, ILogger<MqttPublisher> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.MqttHost, _settings.MqttPort)
            .WithClientId($"OpenCoffee-{Environment.MachineName}")
            .WithCleanSession()
            .Build();
    }

    /// <summary>
    /// Ensures the MQTT client is connected, then publishes the payload
    /// to the configured topic.
    /// </summary>
    public async Task PublishAsync(string json, CancellationToken ct = default)
    {
        if (!_client.IsConnected)
        {
            _logger.LogInformation("Connecting to MQTT broker {Host}:{Port}...",
                _settings.MqttHost, _settings.MqttPort);
            await _client.ConnectAsync(_options, ct);
            _logger.LogInformation("MQTT connected");
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(_settings.MqttTopic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .WithRetainFlag()
            .Build();

        await _client.PublishAsync(message, ct);
        _logger.LogDebug("Published {Bytes} bytes to {Topic}",
            json.Length, _settings.MqttTopic);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_client.IsConnected)
        {
            try { await _client.DisconnectAsync(); }
            catch { /* best effort */ }
        }
        _client.Dispose();
    }
}
