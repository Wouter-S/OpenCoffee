namespace OpenCoffee;

public class Settings
{
    public string MachineIp { get; set; } = "";
    public string Pin { get; set; } = "";
    public string DeviceName { get; set; } = "CSharpCoffee";
    public int PollIntervalMinutes { get; set; } = 5;
    public string MqttHost { get; set; } = "localhost";
    public int MqttPort { get; set; } = 1883;
    public string MqttTopic { get; set; } = "/coffee";
}
