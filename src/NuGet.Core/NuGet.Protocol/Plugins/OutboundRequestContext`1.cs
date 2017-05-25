﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;

namespace NuGet.Protocol.Plugins
{
    /// <summary>
    /// Context for an outbound request.
    /// </summary>
    /// <typeparam name="TResult">The response payload type.</typeparam>
    public sealed class OutboundRequestContext<TResult> : OutboundRequestContext
    {
        private readonly CancellationToken _cancellationToken;
        private readonly ICancellationTokenSource _cancellationTokenSource;
        private readonly IConnection _connection;
        private bool _isClosed;
        private bool _isDisposed;
        private bool _isKeepAlive;
        private readonly Message _request;
        private readonly ITaskCompletionSource<TResult> _taskCompletionSource;
        private readonly TimeSpan? _timeout;
        private readonly Timer _timer;

        /// <summary>
        /// Gets the completion task.
        /// </summary>
        public Task<TResult> CompletionTask => _taskCompletionSource.Task;

        /// <summary>
        /// Initializes a new <see cref="OutboundRequestContext{TResult}" /> class.
        /// </summary>
        /// <param name="connection">A connection.</param>
        /// <param name="request">A request.</param>
        /// <param name="timeout">An optional request timeout.</param>
        /// <param name="isKeepAlive">A flag indicating whether or not the request supports progress notifications
        /// to reset the request timeout.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="connection" />
        /// is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="request" />
        /// is <c>null</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken" />
        /// is cancelled.</exception>
        public OutboundRequestContext(
            IConnection connection,
            Message request,
            TimeSpan? timeout,
            bool isKeepAlive,
            CancellationToken cancellationToken)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            _connection = connection;
            _request = request;
            _taskCompletionSource = NuGetTaskCompletionSource.Create<TResult>($"{nameof(OutboundRequestContext)} {request.RequestId}");
            _timeout = timeout;
            _isKeepAlive = isKeepAlive;
            RequestId = request.RequestId;

            if (timeout.HasValue)
            {
                _timer = new Timer(
                    OnTimeout,
                    state: null,
                    dueTime: timeout.Value,
                    period: Timeout.InfiniteTimeSpan);
            }

            _cancellationTokenSource = PluginCancellationTokenSource.CreateLinkedTokenSource(cancellationToken, $"{nameof(OutboundRequestContext)} {request.RequestId}");

            _cancellationTokenSource.Token.Register(() => Close("Cancelled"));

            // Capture the cancellation token now because if the cancellation token source
            // is disposed race conditions may cause an exception acccessing its Token property.
            _cancellationToken = _cancellationTokenSource.Token;
        }

        /// <summary>
        /// Handles cancellation for the outbound request.
        /// </summary>
        public override void HandleCancel()
        {
            _taskCompletionSource.TrySetCanceled("Received cancellation message");
        }

        /// <summary>
        /// Handles progress notifications for the outbound request.
        /// </summary>
        /// <param name="progress">A progress notification.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="progress" /> is <c>null</c>.</exception>
        public override void HandleProgress(Message progress)
        {
            if (progress == null)
            {
                throw new ArgumentNullException(nameof(progress));
            }

            var payload = MessageUtilities.DeserializePayload<Progress>(progress);

            if (_timeout.HasValue && _isKeepAlive)
            {
                _timer.Change(_timeout.Value, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Handles a response for the outbound request.
        /// </summary>
        /// <param name="response">A response.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="response" /> is <c>null</c>.</exception>
        public override void HandleResponse(Message response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            var payload = MessageUtilities.DeserializePayload<TResult>(response);

            _taskCompletionSource.TrySetResult(payload);
        }

        /// <summary>
        /// Handles a fault response for the outbound request.
        /// </summary>
        /// <param name="fault">A fault response.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="fault" /> is <c>null</c>.</exception>
        public override void HandleFault(Message fault)
        {
            if (fault == null)
            {
                throw new ArgumentNullException(nameof(fault));
            }

            var payload = MessageUtilities.DeserializePayload<Fault>(fault);

            throw new ProtocolException(payload.Message);
        }

        protected override void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                Close("Disposing");

                // Do not dispose of _connection.
            }

            _isDisposed = true;
        }

        private void Close(string reason)
        {
            if (!_isClosed)
            {
                _taskCompletionSource.TrySetCanceled($"Closing {nameof(OutboundRequestContext)}: {reason}");

                if (_timer != null)
                {
                    _timer.Dispose();
                }

                try
                {
                    using (_cancellationTokenSource)
                    {
                        _cancellationTokenSource.Cancel($"Closing {nameof(OutboundRequestContext)}: {reason}");
                    }
                }
                catch (Exception)
                {
                }

                _isClosed = true;
            }
        }

        private void OnTimeout(object state)
        {
            Debug.WriteLine($"Request {_request.RequestId} timed out.");

            _taskCompletionSource.TrySetCanceled("Timed out");
        }
    }
}