using Newtonsoft.Json;

namespace DemoUnixListener
{
    public class JsonRpcRequest<T>
    {
        public string jsonrpc = "2.0";
        [JsonProperty("params")] public T Params { get; set; }
        [JsonProperty("method")] public string Method { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
    }
}