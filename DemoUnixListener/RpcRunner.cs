using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DemoUnixListener
{
    public class RpcRunner
    {
        private readonly Dictionary<string, Func<object, string, CancellationToken, Task<dynamic>>> _methods =
            new Dictionary<string, Func<object, string, CancellationToken, Task<dynamic>>>();

        private readonly Dictionary<string, Type> _parameterTypes =
            new Dictionary<string, Type>();

        public void SetHandlerAsync(string methodName, Func<object, string, CancellationToken, Task<dynamic>> run, Type type)
        {
            _methods.Add(methodName, run);
            _parameterTypes.Add(methodName, type);
        }

        public bool HasHandler(string methodName)
        {
            return _methods.ContainsKey(methodName);
        }

        public async Task<dynamic> RunAsync(string methodName, object rpcParams, string rpcId,
            CancellationToken cancellationToken)
        {
            return await _methods[methodName](rpcParams, rpcId, cancellationToken);
        }

        public Type GetParametersType(string reqMethod)
        {
            return _parameterTypes[reqMethod];
        }
    }
}