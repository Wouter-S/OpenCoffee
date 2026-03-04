# OpenCoffee

A .NET 8 application that connects to Jura coffee machines over WiFi and publishes machine data to an MQTT broker. 

Tested with the **Jura E4**.

## What it does

- Connects to a Jura coffee machine via TCP (port 51515) using the Smart Connect WiFi module
- Authenticates with PIN and session hash
- Polls the machine on a configurable interval and reads:
  - Machine status (coffee ready, alerts)
  - Maintenance status (cleaning, filter, descaling percentages)
  - Maintenance counters
  - Product counters (espresso, coffee, cappuccino, etc.)
- Publishes a JSON snapshot to an MQTT broker (e.g. for Home Assistant)

## Configuration

Copy `appsettings.json` and create an `appsettings.Development.json` with your machine's details:

```json
{
  "Coffee": {
    "MachineIp": "192.168.1.118",
    "Pin": "1234",
    "DeviceName": "OpenCoffee",
    "PollIntervalMinutes": 1,
    "MqttHost": "192.168.1.5",
    "MqttPort": 1883,
    "MqttTopic": "/coffee"
  }
}
```

| Setting | Description |
|---|---|
| `MachineIp` | IP address of your Jura machine's Smart Connect module |
| `Pin` | PIN code configured on the machine (leave empty if none) |
| `DeviceName` | Name this client identifies as to the machine |
| `PollIntervalMinutes` | How often to poll the machine |
| `MqttHost` | MQTT broker hostname or IP |
| `MqttPort` | MQTT broker port |
| `MqttTopic` | MQTT topic to publish to |

## Running

### Directly

```bash
dotnet run
```

### Docker

```bash
docker run \
  --name opencoffee \
  -e Coffee__MachineIp=192.168.1.118 \
  -e Coffee__Pin=0000 \
  -e Coffee__DeviceName=CSharpCoffee \
  -e Coffee__PollIntervalMinutes=1 \
  -e Coffee__MqttHost=192.168.1.5 \
  -e Coffee__MqttPort=1883 \
  -e Coffee__MqttTopic=coffee \
  -v /dockerData/coffee/hash.json:/app/hash.json \
  ghcr.io/wouter-s/opencofee
```

> **Note:** The `hash.json` file must exist on the host before starting the container.
> Create it with `touch /dockerData/coffee/hash.json` if it doesn't exist yet.

### Docker Compose

```yaml
services:
  opencoffee:
    image: ghcr.io/wouter-s/opencofee
    container_name: opencoffee
    restart: unless-stopped
    environment:
      - Coffee__MachineIp=192.168.1.118
      - Coffee__Pin=0000
      - Coffee__DeviceName=CSharpCoffee
      - Coffee__PollIntervalMinutes=1
      - Coffee__MqttHost=192.168.1.5
      - Coffee__MqttPort=1883
      - Coffee__MqttTopic=coffee
    volumes:
      - /dockerData/coffee/hash.json:/app/hash.json
```

### Docker Bake (multi-platform)

```bash
docker buildx bake
```

## First connection

On the first connection the machine will ask you to confirm access physically (press a button on the machine). After that, a session hash is stored in `hash.json` so future connections authenticate automatically.

## Protocol

The communication protocol uses a custom symmetric cipher over TCP. See `CryptoUtil.cs` for the encryption implementation and `TcpClient.cs` for the full protocol documentation.
