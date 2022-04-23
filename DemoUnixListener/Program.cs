using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DemoUnixListener
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSystemdConsole(options => { options.TimestampFormat = "hh:mm:ss.fff "; });
                })
                .ConfigureServices(services => { services.AddHostedService<UnixListenerHostedService>(); })
                .RunConsoleAsync();
        }
    }
}