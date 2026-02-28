using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OpenCoffee;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.Configure<Settings>(
                    context.Configuration.GetSection("Coffee"));

                services.AddSingleton<MqttPublisher>();
                services.AddHostedService<PollingService>();
            })
            .Build();

        await host.RunAsync();
    }
}
