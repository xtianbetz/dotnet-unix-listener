using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DemoUnixListener
{
    internal class DemoUnixListener
    {
        private readonly ILogger _logger;
        private readonly RpcRunner _rpcRunner;

        private async Task<JsonRpcResponse> HandleJsonRpc(string reqJson, CancellationToken cancellationToken)
        {
            JsonRpcRequest<JObject> req = null;
            var resp = new JsonRpcResponse();
            try
            {
                req = JsonConvert.DeserializeObject<JsonRpcRequest<JObject>>(reqJson);
            }
            catch (JsonReaderException jre)
            {
                _logger.LogError("JSON Parsing exception: {Message}", jre.Message);
            }

            if (req == null)
            {
                // TODO: generate uuid here
                resp.Id = "deadbeefcafebabe";
                resp.Error = new JsonRpcError
                {
                    Code = 400,
                    Message = "Bad Request"
                };

                return resp;
            }

            _logger.LogInformation("JSON-RPC Request ID is {Id}", req.Id);

            if (_rpcRunner.HasHandler(req.Method))
            {
    
                resp.Id = req.Id;
                try
                {
                    // Attempt to coerce the type of req.Params
                    var typeName = _rpcRunner.GetParametersType(req.Method);
                    try
                    {
                        var betterParams = req.Params.ToObject(typeName);
                        resp.Result = await _rpcRunner.RunAsync(req.Method, betterParams, req.Id, cancellationToken);
                    }
                    catch (JsonSerializationException)
                    {
                        // Exceptions for missing parameters will be thrown using the annotations above
                        _logger.LogError("Client sent bad params {Json}", JsonConvert.SerializeObject(req.Params));
                        resp.Error = new JsonRpcError
                        {
                            Code = 400,
                            Message = "Bad params object in request"
                        }; 
                    }

                }
                catch (RpcFailedException rfe)
                {
                    resp.Error = new JsonRpcError
                    {
                        Code = rfe.ErrorCode,
                        Message = rfe.Message
                    };
                }
            }
            else
            {
                resp.Id = req.Id;
                resp.Error = new JsonRpcError
                {
                    Code = 501,
                    Message = $"Unknown method {req.Method}"
                };
            }

            return resp;
        }

        public DemoUnixListener(ILogger logger)
        {
            _logger = logger;
            _rpcRunner = new RpcRunner();
            _rpcRunner.SetHandlerAsync("MyCompany.MyApp.Foo",
                async (rpcParams, rpcId, anotherCancellationToken) =>
                {
                    _logger.LogInformation("Got a Foo, waiting a bit!");
                    
                    
                    var properlyTypedParams = (FooRequestMethodParams) rpcParams;
                    var answer =  properlyTypedParams.ParamOne + 42;

                    if (properlyTypedParams.ParamTwo == "Badgers")
                    {
                        throw new RpcFailedException("We don't need no stinkin' badgers!", 1000);
                    }
                    
                    await Task.Delay(1000);
                    
                    _logger.LogInformation("Client sent params: {ParamOne} and {ParamTwo}", properlyTypedParams.ParamOne, properlyTypedParams.ParamTwo);
                    return answer;
                }, typeof(FooRequestMethodParams));
        }

        public async Task Run(string unixSocketPath, CancellationToken cancellationToken)
        {
            var recvSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            System.IO.File.Delete(unixSocketPath);
            var ep = new UnixDomainSocketEndPoint(unixSocketPath);
            recvSocket.Bind(ep);
            recvSocket.Listen(1024);

            _logger.LogInformation("Waiting for clients...");

            // Sadly, only later .NET versions have cancellation token support in AcceptAsync
            // TODO: use cancellation token to Make this code better for later .NET versions
            var acceptorTask = recvSocket.AcceptAsync();
            var clientTasksById = new Dictionary<int, Task>();

            const int idleTimerMs = 15000;
            var idleTimerTask = Task.Delay(idleTimerMs, cancellationToken);
            var allRunningTasks = new List<Task> {idleTimerTask, acceptorTask};

            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Waiting on {Count} tasks", allRunningTasks.Count);
                var finishedTask = await Task.WhenAny(allRunningTasks);

                if (finishedTask.Id == idleTimerTask.Id)
                {
                    // Timer is done, re-arm it
                    await idleTimerTask;
                    idleTimerTask = Task.Delay(idleTimerMs, cancellationToken);
                }
                else if (finishedTask.Id == acceptorTask.Id)
                {
                    var clientSock = await acceptorTask;
                    _logger.LogInformation("Got a new client. Waiting to read data from socket");

                    // Schedule a new task to receive the clients data AND process it AND send a reply
                    var clientTask = ReceiveAndProcessAndReply(clientSock, cancellationToken);
                    clientTasksById[clientTask.Id] = clientTask;

                    // Keep listening for more clients
                    acceptorTask = recvSocket.AcceptAsync();
                }
                else
                {
                    await finishedTask;
                    foreach (var clientTask in clientTasksById.Values.Where(clientTask => finishedTask.Id == clientTask.Id))
                    {
                        _logger.LogInformation("Done with client task {Id}", clientTask.Id);
                        clientTasksById.Remove(clientTask.Id);
                    }
                }

                // Recreate the list of all running tasks
                allRunningTasks = new List<Task> {idleTimerTask, acceptorTask};
                allRunningTasks.AddRange(clientTasksById.Values);
            }
        }

        private async Task ReceiveAndProcessAndReply(Socket clientSock, CancellationToken cancellationToken)
        {
            try
            {
                // Schedule a new task to receive the client's data
                var buf = new byte[1024];
                var totalBytesReadCount = 0;
                while (true)
                {
                    var bytesReadCount = await clientSock.ReceiveAsync(buf.AsMemory(totalBytesReadCount),
                        SocketFlags.None,
                        cancellationToken);

                    if (bytesReadCount == 0)
                    {
                        _logger.LogInformation("Client closed connection");
                        break;
                    }

                    if (System.Text.Encoding.UTF8.GetString(buf).Contains("\n\n"))
                    {
                        _logger.LogInformation("Client done sending data");
                        break;
                    }

                    totalBytesReadCount += bytesReadCount;
                }

                _logger.LogInformation("Received {Buffer}", System.Text.Encoding.UTF8.GetString(buf));

                var response = await HandleJsonRpc(System.Text.Encoding.UTF8.GetString(buf), cancellationToken);
                var bytesToSend = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response) + "\n\n");
                await clientSock.SendAsync(bytesToSend, SocketFlags.None, cancellationToken);

                clientSock.Close();
            }
            catch (SocketException se)
            {
                _logger.LogInformation("Got socket exception: {Message}", se.Message);
            }
        }
    }
}