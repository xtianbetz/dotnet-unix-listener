using System;

namespace DemoUnixListener
{
    internal class RpcFailedException : Exception
    {
        public int ErrorCode { get; }

        public RpcFailedException(string message, int errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }
    }
}