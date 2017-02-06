using System;

namespace HookComm
{
    public class MessageReceivedEventArgs : EventArgs
    {
        public MessageReceivedEventArgs(Message message)
        {
            Message = message;
        }

        public Message Message { get; }
    }
}