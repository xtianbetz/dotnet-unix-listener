using Newtonsoft.Json;

namespace DemoUnixListener
{
    public class JsonRpcError
    {
        [JsonProperty("code")] public int Code;

        [JsonProperty("message")] public string Message;
    }
}