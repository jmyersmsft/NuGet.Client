using Newtonsoft.Json.Linq;

namespace HookComm
{
    public class Message
    {
        public MessageHeader Header { get; }
        public JToken Payload { get; }

        public Message(MessageHeader header, JToken payload)
        {
            Header = header;
            Payload = payload;
        }
    }
}