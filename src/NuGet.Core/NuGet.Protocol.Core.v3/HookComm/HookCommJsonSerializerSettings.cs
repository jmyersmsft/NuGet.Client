using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HookComm
{
    internal static class HookCommJsonSerializerSettings
    {
        public static readonly JsonSerializerSettings Default = new JsonSerializerSettings
        {
            Converters = new JsonConverter[] {new StringEnumConverter()}
        };
    }
}