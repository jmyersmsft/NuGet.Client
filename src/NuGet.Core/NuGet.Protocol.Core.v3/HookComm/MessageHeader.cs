using System;

namespace HookComm
{
    public class MessageHeader
    {
        public MessageType MessageType { get; set; }


        public Guid RequestId { get; set; }
        public long ContentLength { get; set; }
        public string Method { get; set; }
    }
}