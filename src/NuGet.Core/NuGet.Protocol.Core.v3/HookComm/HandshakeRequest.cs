using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Serialization;

namespace HookComm
{
    public class HandshakeRequest
    {
        public int ProtocolVersion { get; set; }

        public int MinProtocolVersion { get; set; }

        public IEnumerable<string> Methods { get; set; }
    }
}
