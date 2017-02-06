using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HookComm
{
    public class Sender
    {

        private readonly TextWriter writer;
        private readonly BlockingCollection<Message> sendQueue = new BlockingCollection<Message>();
        private Task sendThread;
        private readonly ILogger logger;

        public Sender(TextWriter writer, ILogger logger)
        {
            this.writer = writer;
            this.logger = logger;
        }

        public Task ConnectAsync()
        {
            sendThread = Task.Factory.StartNew(SendThreadAsync, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);
            return Task.FromResult(0);
        }

        private Task SendThreadAsync()
        {
            try
            {
                //_logger.Log("Starting send thread");
                var jsonSerializer = JsonSerializer.Create(HookCommJsonSerializerSettings.Default);
                using (var jsonWriter = new JsonTextWriter(writer))
                {
                    jsonWriter.CloseOutput = false;
                    foreach (var message in sendQueue.GetConsumingEnumerable())
                    {
                        //logger.Log($"Sending {message.Header.MessageType} {message.Header.Method} {message.Header.RequestId}");

                        jsonSerializer.Serialize(jsonWriter, message.Header);
                        writer.WriteLine();
                        jsonSerializer.Serialize(jsonWriter, message.Payload);
                        writer.WriteLine();
                        //logger.Log($"Sent {message.Header.MessageType} {message.Header.Method} {message.Header.RequestId}");

                        writer.Flush();

                        //_logger.Log($"Flushed after {message.Header.MessageType} {message.Header.RequestId}");
                    }
                }

                return Task.FromResult(0);
            }
            catch (Exception ex) when (LogException(ex))
            {
                // unreachable -- LogException returns false
                throw;
            }
        }

        private bool LogException(Exception ex)
        {
            logger.Log(ex.ToString());
            return false;
        }

        public Task SendAsync(Message message)
        {
            //logger.Log($"Queueing {message.Header.MessageType} {message.Header.Method} {message.Header.RequestId}");
            sendQueue.Add(message);
            return Task.FromResult(0);
        }

        public async Task CloseAsync()
        {
            sendQueue.CompleteAdding();
            await sendThread;
            writer.Dispose();
        }
    }
}