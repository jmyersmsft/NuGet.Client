using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

//using static System.FormattableString;

namespace HookComm
{
    public class Connection
    {
        private readonly Sender sender;
        private readonly Receiver receiver;

        private int connectionState = (int)ConnectionState.ReadyToConnect;
        public ConnectionState ConnectionState => (ConnectionState)connectionState;
        private readonly TaskCompletionSource<object> remoteHandshakeReceivedEvent = new TaskCompletionSource<object>();

        private static IReadOnlyList<string> _specialMethods = new[]
        {
            "Handshake"
        };

        public Connection(
            [NotNull] Sender sender,
            [NotNull] Receiver receiver,
            [NotNull] IImmutableDictionary<string, ICommandHandler> handlers,
            [NotNull] ILogger logger)
        {
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            this.sender = sender;
            this.receiver = receiver;
            this.logger = logger;
            this.handlers = handlers.Add("Handshake", new HandshakeHandler());
        }

        private class HandshakeHandler : CommandHandlerBase<HandshakeRequest, HandshakeResponse>
        {
            public override Task<HandshakeResponse> HandleRequest(HandshakeRequest requestPayload, ICommandResponder commandResponder)
            {
                return Task.FromResult(new HandshakeResponse());
            }
        }

        public async Task ConnectAsync()
        {
            receiver.MessageReceived += ReceiverOnMessageReceived;
            await Task.WhenAll(receiver.ConnectAsync(), sender.ConnectAsync());
            await Task.WhenAll(SendHandshakeAsync(), remoteHandshakeReceivedEvent.Task);
        }

        private readonly IImmutableDictionary<string, ICommandHandler> handlers;

        private readonly ConcurrentDictionary<Guid, OutgoingRequestContext> outgoingRequests = new ConcurrentDictionary<Guid, OutgoingRequestContext>();
        private readonly ILogger logger;

        private abstract class OutgoingRequestContext
        {
            public OutgoingRequestContext(Guid requestId)
            {
                RequestId = requestId;
            }

            public Guid RequestId { get; }

            public abstract void HandleResponse(JToken response);
            public abstract void HandleFault(Exception ex);
        }

        private class OutgoingRequestContext<TResult> : OutgoingRequestContext
        {
            public OutgoingRequestContext(Guid requestId) : base(requestId)
            {
            }

            private readonly TaskCompletionSource<TResult> taskCompletionSource = new TaskCompletionSource<TResult>();

            public Task<TResult> CompletionTask => taskCompletionSource.Task;
            public override void HandleResponse(JToken response)
            {
                try
                {
                    taskCompletionSource.SetResult(response.ToObject<TResult>());
                }
                catch (Exception ex)
                {
                    taskCompletionSource.TrySetException(ex);
                }
            }

            public override void HandleFault(Exception ex)
            {
                taskCompletionSource.TrySetException(ex);
            }
        }

        private async Task<HandshakeResponse> SendHandshakeAsync()
        {
            return await SendRequestAndWaitForResponseAsync<HandshakeRequest, HandshakeResponse>(
                "Handshake",
                new HandshakeRequest());
        }

        public async Task<TResult> SendRequestAndWaitForResponseAsync<TRequest, TResult>(string method, TRequest payload)
        {
            var messageHeader = new MessageHeader
            {
                Method = method,
                MessageType = MessageType.Request,
                RequestId = Guid.NewGuid()
            };

            var message = new Message(messageHeader, JToken.FromObject(payload));
            var requestContext = new OutgoingRequestContext<TResult>(messageHeader.RequestId);
            outgoingRequests.TryAdd(messageHeader.RequestId, requestContext);
            await sender.SendAsync(message);

            return await requestContext.CompletionTask;
        }

        private void ReceiverOnMessageReceived(object sender, MessageReceivedEventArgs messageReceivedEventArgs)
        {
            var message = messageReceivedEventArgs.Message;
            OutgoingRequestContext requestContext;
            //_logger.Log(Invariant($"Received {message.Header.MessageType} {message.Header.RequestId}"));
            if (outgoingRequests.TryGetValue(message.Header.RequestId, out requestContext))
            {
                //_logger.Log("found request context");
            }

            switch (message.Header.MessageType)
            {
                case MessageType.Close:
                    HandleCloseMessage(message);
                    break;
                case MessageType.ErrorResponse:
                    HandleErrorResponseMessage(message, requestContext);
                    break;
                case MessageType.IntermediateResultResponse:
                    HandleIntermediateResultResponseMessage(message, requestContext);
                    break;
                case MessageType.ProgressResponse:
                    HandleProgressResponseMessage(message, requestContext);
                    break;
                case MessageType.SuccessResponse:
                    HandleSuccessResponse(message, requestContext);
                    break;
                case MessageType.Cancel:
                    HandleCancelRequest(message, requestContext);
                    break;
                case MessageType.Request:
                    HandleIncomingRequest(message);
                    break;
                default:
                    throw new NotSupportedException("FOO");
            }
        }

        private void HandleCancelRequest(Message message, OutgoingRequestContext requestContext)
        {
            throw new NotImplementedException();
        }

        private void HandleIncomingRequest(Message message)
        {
            ICommandHandler handler;
            if (!handlers.TryGetValue(message.Header.Method, out handler))
            {
                throw new Exception($"No handler for {message.Header.Method}");
            }

            //var sw = System.Diagnostics.Stopwatch.StartNew();
            Task.Factory.StartNew(async () =>
            {
                var result = await handler.HandleRequestAsync(message.Payload, new CommandResponder(this));
                await sender.SendAsync(new Message(
                    new MessageHeader
                    {
                        MessageType = MessageType.SuccessResponse,
                        Method = message.Header.Method,
                        RequestId = message.Header.RequestId
                    },
                    result
                ));

                if(message.Header.Method == "Handshake")
                {
                    remoteHandshakeReceivedEvent.SetResult(null);
                }
            });
            //logger.Log($"Waited {sw.Elapsed} for handler to start");
        }

        private void HandleSuccessResponse(Message message, OutgoingRequestContext requestContext)
        {
            CheckRequestContextNotNull(requestContext);

            Task.Run(() => requestContext.HandleResponse(message.Payload));
        }

        [ContractAnnotation(@"requestContext:null => stop")]
        private static void CheckRequestContextNotNull(OutgoingRequestContext requestContext)
        {
            if (requestContext == null)
            {
                throw new RequestContextNotFoundForResponseException();
            }
        }

        private void HandleProgressResponseMessage(Message message, OutgoingRequestContext requestContext)
        {
            CheckRequestContextNotNull(requestContext);
            throw new NotImplementedException();
        }

        private void HandleIntermediateResultResponseMessage(Message message, OutgoingRequestContext requestContext)
        {
            CheckRequestContextNotNull(requestContext);
            throw new NotImplementedException();
        }

        private void HandleErrorResponseMessage(Message message, OutgoingRequestContext requestContext)
        {
            CheckRequestContextNotNull(requestContext);
            throw new NotImplementedException();
        }

        private void HandleCloseMessage(Message message)
        {
            CloseAsync();
        }

        private class CommandResponder : ICommandResponder
        {
            public CommandResponder(Connection connection)
            {
                Connection = connection;
            }

            public Connection Connection { get; }
        }

        public async Task SendCloseMessageAsync()
        {
            var messageHeader = new MessageHeader
            {
                Method = "Close",
                MessageType = MessageType.Close,
                RequestId = Guid.NewGuid()
            };

            var message = new Message(messageHeader, new JObject());
            await sender.SendAsync(message);
        }

        private readonly TaskCompletionSource<object> closeEvent = new TaskCompletionSource<object>();

        public Task CloseAsync()
        {
            var currentState = connectionState;
            Interlocked.MemoryBarrier();

            if(currentState == (int)ConnectionState.Closing || currentState == (int)ConnectionState.Closed)
            {
                return WaitForCloseAsync();
            }

            var previous = Interlocked.CompareExchange(ref connectionState, (int)ConnectionState.Closing, currentState);
            if (previous == currentState)
            {
                //_logger.Log($"{(ConnectionState)previous} => {ConnectionState}");
                Task.WhenAll(sender.CloseAsync(), receiver.CloseAsync())
                    .ContinueWith(
                        _ =>
                        {
                            connectionState = (int)ConnectionState.Closed;
                            //_logger.Log(" => Closed");
                            if (_.IsCanceled)
                            {
                                closeEvent.TrySetCanceled();
                            }
                            if (_.IsFaulted)
                            {
                                closeEvent.TrySetException(_.Exception);
                            }
                            else
                            {
                                closeEvent.SetResult(null);
                            }
                        });
            }

            return WaitForCloseAsync();
        }

        public Task WaitForCloseAsync()
        {
            //_logger.Log("Wait for exit");
            return closeEvent.Task;
        }
    }

}