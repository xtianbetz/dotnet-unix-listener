using Newtonsoft.Json;

namespace DemoUnixListener
{
    public class FooRequestMethodParams
    {
        [JsonProperty(Required = Required.Always)]
        public int ParamOne;
        [JsonProperty(Required = Required.Always)]
        public string ParamTwo;
    }
}