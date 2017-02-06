using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HookComm
{
    public class Receiver
    {

        private readonly TextReader reader;
        private readonly ILogger logger;
        private Task receiveThread;
        private bool closing;

        public Receiver(TextReader reader, ILogger logger)
        {
            this.reader = reader;
            this.logger = logger;
        }

        public Task ConnectAsync()
        {
            receiveThread = Task.Factory.StartNew(ReceiveThreadAsync, TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning);
            return Task.FromResult(0);
        }

        private Task ReceiveThreadAsync()
        {
            try
            {
                //_logger.Log("Starting receive thread");
                var jsonSerializer = JsonSerializer.Create(HookCommJsonSerializerSettings.Default);

                using (var jsonReader = new JsonTextReader(reader))
                {
                    jsonReader.SupportMultipleContent = true;
                    jsonReader.CloseInput = false;

                    while (true)
                    {
                        if (!jsonReader.Read())
                        {
                            //_logger.Log("End of file");
                            return Task.FromResult(0);
                        }
                        if (closing)
                        {
                            //_logger.Log("Closing");
                            return Task.FromResult(0);
                        }
                        
                        var header = jsonSerializer.Deserialize<MessageHeader>(jsonReader);
                        //logger.Log($"Received header for {header.MessageType} {header.Method} {header.RequestId}");

                        if (!jsonReader.Read())
                        {
                            throw new Exception("End of file after header but before payload");
                        }
                        if (closing)
                        {
                            //_logger.Log("Closing");
                            return Task.FromResult(0);
                        }

                        var payload = JToken.Load(jsonReader);

                        //logger.Log($"Received payload for {header.MessageType} {header.Method} {header.RequestId}");

                        MessageReceived?.Invoke(this, new MessageReceivedEventArgs(new Message(header, payload)));
                    }
                }
            }
            catch (Exception ex) when (LogException(ex))
            {
                // note: LogException returns false
                throw;
            }
        }

        private bool LogException(Exception ex)
        {
            logger.Log(ex.ToString());
            return false;
        }
        private async Task<MessageHeader> ReadHeader()
        {
            string headerLine;
            do
            {
                headerLine = await reader.ReadLineAsync().ConfigureAwait(false);
            } while (string.IsNullOrWhiteSpace(headerLine));

            var parts = headerLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                throw new Exception("not enough parts");
            }

            var messageTypePart = parts[0];
            var methodPart = parts[1];
            var requestIdPart = parts[2];
            var contentLengthPart = parts[3];

            MessageType messageType;
            if (!Enum.TryParse(messageTypePart, out messageType))
            {
                throw new Exception($"unknown message type {messageTypePart}");
            }

            Guid requestId;
            if (!Guid.TryParse(requestIdPart, out requestId))
            {
                throw new Exception($"malformed request ID");
            }

            long contentLength;
            if (!long.TryParse(contentLengthPart, out contentLength) || contentLength < 0)
            {
                throw new Exception($"malformed content length");
            }

            return new MessageHeader
            {
                MessageType = messageType,
                Method = methodPart,
                RequestId = requestId,
                ContentLength = contentLength
            };
        }

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        public Task CloseAsync()
        {
            closing = true;
            reader.Dispose();
            // don't wait for the read thread -- there's no good way to abort the pending read.
            return Task.FromResult(0);
        }
    }
}