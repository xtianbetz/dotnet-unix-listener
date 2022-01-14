using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DemoUnixListener
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var recvSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            var unixSocketpath = "foo.sock";
            System.IO.File.Delete(unixSocketpath);
            var ep = new UnixDomainSocketEndPoint(unixSocketpath);
            recvSocket.Bind(ep);
            recvSocket.Listen(1024);

            var cts = new CancellationTokenSource();
            
            Console.WriteLine("Waiting for clients...");

            // Sadly, only later .NET versions have cancellation token support in AcceptAsync
            // TODO: use cancellation token to Make this code better for later .NET versions
            var accepterTask = recvSocket.AcceptAsync();
            var clientReceiveTasksById = new Dictionary<int, Task>();
            var clientSendTasksById = new Dictionary<int, Task>();

            var idleTimerMs = 15000;
            var idleTimerTask = Task.Delay(idleTimerMs);
            var bytesToSend = System.Text.Encoding.UTF8.GetBytes("hi\n").ToArray();
            
            var allRunningTasks = new List<Task>();
            allRunningTasks.Add(idleTimerTask);
            allRunningTasks.Add(accepterTask);

            var clientSocksByTaskId = new Dictionary<int, Socket>();
            var clientBufsByTaskId = new Dictionary<int, byte[]>();
            
            while (true)
            {
                Console.WriteLine("Waiting on {0} tasks", allRunningTasks.Count);
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
                    Console.WriteLine("Got a new client. Waiting to read data from socket.");

                    // Schedule a new task to receive the client's data
                    var buf = new byte[1024];
                    var clientReceiveTask = clientSock.ReceiveAsync(buf.AsMemory(), SocketFlags.None, cts.Token).AsTask();
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
                            Console.WriteLine("Client said: {0}", System.Text.Encoding.UTF8.GetString(msg));
                            clientBufsByTaskId.Remove(clientReceiveTask.Id);

                            // Schedule a new task to send a response
                            var clientSock = clientSocksByTaskId[clientReceiveTask.Id];
                            var clientSendTask = clientSock.SendAsync(bytesToSend, SocketFlags.None, cts.Token).AsTask();
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
                            Console.WriteLine("Finished sending data to client, closing socket.");
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
}