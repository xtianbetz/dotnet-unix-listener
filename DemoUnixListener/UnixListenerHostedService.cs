using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DemoUnixListener
{
    internal class UnixListenerHostedService : IHostedService
    {
        private readonly ILogger<IHostedService> _logger;
        private readonly IHostApplicationLifetime _applicationLifetime;

        public UnixListenerHostedService(ILogger<UnixListenerHostedService> logger,
            IHostApplicationLifetime applicationLifetime)
        {
            _logger = logger;
            _applicationLifetime = applicationLifetime;
        }

        private async Task HandleCommandLineArguments(CancellationToken cancellationToken)
        {
            const string unixSocketPath = "dotnet-unix-listener.sock";
            var listener = new DemoUnixListener(_logger);

            try
            {
                await listener.Run(unixSocketPath, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogInformation("Shutting Down from exception: {Exception}", e);
                _applicationLifetime.StopApplication();
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _applicationLifetime.ApplicationStarted.Register(() =>
            {
                Task.Run(async () => { await HandleCommandLineArguments(cancellationToken); }, cancellationToken);
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Exiting with return code 0");
            Environment.ExitCode = 0;
            return Task.CompletedTask;
        }
    }
}