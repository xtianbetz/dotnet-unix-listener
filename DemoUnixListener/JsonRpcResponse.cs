using Newtonsoft.Json;

namespace DemoUnixListener
{
    public class JsonRpcResponse
    {
        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public dynamic Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public dynamic Error { get; set; }

        [JsonProperty("id")] public string Id { get; set; }
    }
}