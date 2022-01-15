using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace DemoUnixListener
{
    class DemoUnixListener
    {
        private ILogger _logger;
            
        public DemoUnixListener(ILogger logger)
        {
            _logger = logger;
        }
        
        public async Task Run(string unixSocketpath, CancellationToken cancellationToken)
        {
            var recvSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            System.IO.File.Delete(unixSocketpath);
            var ep = new UnixDomainSocketEndPoint(unixSocketpath);
            recvSocket.Bind(ep);
            recvSocket.Listen(1024);
           
            _logger.LogInformation("Waiting for clients...");

            // Sadly, only later .NET versions have cancellation token support in AcceptAsync
            // TODO: use cancellation token to Make this code better for later .NET versions
            var accepterTask = recvSocket.AcceptAsync();
            var clientReceiveTasksById = new Dictionary<int, Task>();
            var clientSendTasksById = new Dictionary<int, Task>();

            var idleTimerMs = 15000;
            var idleTimerTask = Task.Delay(idleTimerMs, cancellationToken);
            var bytesToSend = System.Text.Encoding.UTF8.GetBytes("hi\n").ToArray();
            
            var allRunningTasks = new List<Task>();
            allRunningTasks.Add(idleTimerTask);
            allRunningTasks.Add(accepterTask);

            var clientSocksByTaskId = new Dictionary<int, Socket>();
            var clientBufsByTaskId = new Dictionary<int, byte[]>();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Waiting on {0} tasks", allRunningTasks.Count);
                var finishedTask = await Task.WhenAny(allRunningTasks);

                if (finishedTask.Id == idleTimerTask.Id)
                {
                    // Timer is done, re-arm it
                    await idleTimerTask;
                    idleTimerTask = Task.Delay(idleTimerMs);
                }
                else if (finishedTask.Id == accepterTask.Id)
                {
                    var clientSock = await accepterTask;
                    _logger.LogInformation("Got a new client. Waiting to read data from socket");

                    // Schedule a new task to receive the client's data
                    var buf = new byte[1024];
                    var clientReceiveTask = clientSock.ReceiveAsync(buf.AsMemory(), SocketFlags.None, cancellationToken).AsTask();
                    clientSocksByTaskId[clientReceiveTask.Id] = clientSock;
                    clientBufsByTaskId[clientReceiveTask.Id] = buf;
                    clientReceiveTasksById[clientReceiveTask.Id] = clientReceiveTask;
                   
                    // Keep listening for more clients
                    accepterTask = recvSocket.AcceptAsync();
                }
                else
                {
                    await finishedTask;
                    foreach (var clientReceiveTask in clientReceiveTasksById.Values)
                    {
                        if (finishedTask.Id == clientReceiveTask.Id)
                        {
                            var msg = clientBufsByTaskId[clientReceiveTask.Id];
                            _logger.LogInformation($"Client said: {0}", System.Text.Encoding.UTF8.GetString(msg));
                            clientBufsByTaskId.Remove(clientReceiveTask.Id);

                            // Schedule a new task to send a response
                            var clientSock = clientSocksByTaskId[clientReceiveTask.Id];
                            var clientSendTask = clientSock.SendAsync(bytesToSend, SocketFlags.None, cancellationToken).AsTask();
                            clientSendTasksById[clientSendTask.Id] = clientSendTask;

                            // Re-associate our socket with the sending task
                            clientSocksByTaskId.Remove(clientReceiveTask.Id);
                            clientSocksByTaskId[clientSendTask.Id] = clientSock;

                            // Remove this task so we don't await it again.
                            clientReceiveTasksById.Remove(clientReceiveTask.Id);
                            break;
                        }
                    }

                    foreach (var clientSendTask in clientSendTasksById.Values)
                    {
                        if (finishedTask.Id == clientSendTask.Id)
                        {
                            _logger.LogInformation("Finished sending data to client, closing socket");
                            clientSocksByTaskId[clientSendTask.Id].Close();
                            
                            clientSocksByTaskId.Remove(clientSendTask.Id);
                            
                            // Remove this task so we don't await it again.
                            clientSendTasksById.Remove(clientSendTask.Id);
                            break;
                        }
                    }
                }

                // Recreate the list of all running tasks
                allRunningTasks = new List<Task>();
                allRunningTasks.Add(idleTimerTask);
                allRunningTasks.Add(accepterTask);
                foreach (var item in clientReceiveTasksById.Values)
                {
                    allRunningTasks.Add(item); 
                }
                foreach (var item in clientSendTasksById.Values)
                {
                    allRunningTasks.Add(item); 
                }
            }            
        }
    }

    class UnixListenerHostedService : IHostedService
    {
        private ILogger<IHostedService> _logger;
        private CancellationToken _token;
        private IHostApplicationLifetime _applicationLifetime; 

        public UnixListenerHostedService(ILogger<UnixListenerHostedService> logger,
            IHostApplicationLifetime applicationLifetime)
        {
            _logger = logger;
            _applicationLifetime = applicationLifetime;
        }

        public async Task HandleCommandLineArguments(CancellationToken cancellationToken)
        {
            var unixSocketpath = "foo.sock";
            var listener = new DemoUnixListener(_logger);

            _token = cancellationToken;

            try
            {
                await listener.Run(unixSocketpath, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Shutting Down");
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
    
    class Program
    {
        static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSystemdConsole(options =>
                    {
                        options.TimestampFormat = "hh:mm:ss.fff ";
                    });
                })
                .ConfigureServices(services =>
                {
                    services.AddHostedService<UnixListenerHostedService>();
                })
                .RunConsoleAsync();
        }
    }
}