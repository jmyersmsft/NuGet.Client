// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    /// <summary>
    /// Represents a unidirectional communications channel to a target.
    /// </summary>
    /// <remarks>
    /// Any public static members of this type are thread safe.
    /// Any instance members are not guaranteed to be thread safe.
    /// </remarks>
    public sealed class Sender : ISender
    {
        private bool _hasConnected;
        private bool _isClosed;
        private bool _isDisposed;
        private readonly object _sendLock;
        private readonly TextWriter _textWriter;

        /// <summary>
        /// Instantiates a new <see cref="Sender" /> class.
        /// </summary>
        /// <param name="writer">A text writer.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="writer" /> is <c>null</c>.</exception>
        public Sender(TextWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            _textWriter = writer;
            _sendLock = new object();
        }

        /// <summary>
        /// Disposes of this instance.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Close();

            loggerThread.Dispose();
            MessageTracker.Instance.Dispose();
            _textWriter.Dispose();

            GC.SuppressFinalize(this);

            _isDisposed = true;
        }

        /// <summary>
        /// Closes the connection.
        /// </summary>
        /// <remarks>This does not call <see cref="IDisposable.Dispose" />.</remarks>
        public void Close()
        {
            _isClosed = true;
        }

        /// <summary>
        /// Connects.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if this object is disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if this object is closed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if this method has already been called.</exception>
        public void Connect()
        {
            ThrowIfDisposed();

            if (_isClosed)
            {
                throw new InvalidOperationException(Strings.Plugin_ConnectionIsClosed);
            }

            if (_hasConnected)
            {
                throw new InvalidOperationException(Strings.Plugin_ConnectionAlreadyStarted);
            }

            _hasConnected = true;
        }

        AsyncLoggerThread loggerThread = new AsyncLoggerThread("Sender");

        /// <summary>
        /// Asynchronously sends a message to the target.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if this object is disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="Connect" /> has not been called.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="message" /> is <c>null</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken" />
        /// is cancelled.</exception>
        public Task SendAsync(Message message, CancellationToken cancellationToken)
        {
            var stopWatch = System.Diagnostics.Stopwatch.StartNew();

            ThrowIfDisposed();

            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!_hasConnected)
            {
                throw new InvalidOperationException(Strings.Plugin_NotConnected);
            }

            if (!_isClosed)
            {
                lock (_sendLock)
                {
                    using (var jsonWriter = new JsonTextWriter(_textWriter))
                    {
                        jsonWriter.CloseOutput = false;

                        JsonSerializationUtilities.Serialize(jsonWriter, message);

                        // We need to terminate JSON objects with a delimiter (i.e.:  a single
                        // newline sequence) to signal to the receiver when to stop reading.
                        _textWriter.WriteLine();
                        _textWriter.Flush();
                    }
                }
            }

            return Task.FromResult(0);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(Sender));
            }
        }
    }

    public class AsyncLoggerThread : IDisposable
    {
        private BlockingCollection<object> queue = new BlockingCollection<object>(new ConcurrentQueue<object>());
        private ManualResetEventSlim done = new ManualResetEventSlim();
        private Stopwatch timer = Stopwatch.StartNew();

        public void Enqueue(object obj)
        {
            queue.Add(new {Timestamp = timer.Elapsed, Data = obj});
        }

        public AsyncLoggerThread(string name)
        {
            Task.Factory.StartNew(
                () =>
                {
                    using (var file = new StreamWriter(File.OpenWrite($@"c:\temp\timings.{name}.{Process.GetCurrentProcess().ProcessName}.{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{Process.GetCurrentProcess().Id}.{Guid.NewGuid():N}.log")))
                    {
                        try
                        {
                            file.WriteLine("{\"entries\":[null");
                            foreach (var obj in queue.GetConsumingEnumerable())
                            {
                                file.WriteLine("," + JsonConvert.SerializeObject(obj));
                                file.Flush();
                            }

                        }
                        finally
                        {
                            file.WriteLine("]}");
                            done.Set();
                        }
                    }
                },
                TaskCreationOptions.LongRunning
                );
        }

        public void Dispose()
        {
            queue.CompleteAdding();
            //done.Wait();
        }

    }
}