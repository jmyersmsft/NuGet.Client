﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Protocol.Plugins
{
    /// <summary>
    /// Represents a unidirectional communications channel from a target.
    /// </summary>
    public interface IReceiver : IDisposable
    {
        /// <summary>
        /// Occurs when an unrecoverable fault has been caught.
        /// </summary>
        event EventHandler<ProtocolErrorEventArgs> Faulted;

        /// <summary>
        /// Occurs when a message has been received.
        /// </summary>
        event EventHandler<MessageEventArgs> MessageReceived;

        /// <summary>
        /// Asynchronously closes the connection.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if this object is disposed.</exception>
        Task CloseAsync();

        /// <summary>
        /// Asynchronously connects.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if this object is disposed.</exception>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken" />
        /// is cancelled.</exception>
        Task ConnectAsync(CancellationToken cancellationToken);
    }
}