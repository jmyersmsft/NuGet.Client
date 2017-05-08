// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace NuGet.Protocol.Plugins
{
    /// <summary>
    /// Represents a unidirectional communications channel from a target.
    /// </summary>
    public abstract class Receiver : IReceiver
    {
        /// <summary>
        /// Gets a flag indicating whether or not this instance is closed.
        /// </summary>
        protected bool IsClosed { get; private set; }

        /// <summary>
        /// Gets or sets a flag indicating whether or not this instance is disposed.
        /// </summary>
        protected bool IsDisposed { get; set; }

        /// <summary>
        /// Occurs when an unrecoverable fault has been caught.
        /// </summary>
        public event EventHandler<ProtocolErrorEventArgs> Faulted;

        /// <summary>
        /// Occurs when a message has been received.
        /// </summary>
        public event EventHandler<MessageEventArgs> MessageReceived;

        /// <summary>
        /// Closes the connection.
        /// </summary>
        /// <remarks>This does not call <see cref="IDisposable.Dispose" />.</remarks>
        public virtual void Close()
        {
            IsClosed = true;
        }

        /// <summary>
        /// Connects.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if this object is disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if this object is closed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if this method has already been called.</exception>
        public abstract void Connect();

        /// <summary>
        /// Disposes of this instance.
        /// </summary>
        public abstract void Dispose();

        protected void FireFaultEvent(Exception exception, Message message)
        {
            var ex = new ProtocolException(Strings.Plugin_ProtocolException, exception);
            var eventArgs = message == null
                ? new ProtocolErrorEventArgs(ex) : new ProtocolErrorEventArgs(ex, message);

            Faulted?.Invoke(this, eventArgs);
        }

        protected void FireMessageReceivedEvent(Message message)
        {
            MessageReceived?.Invoke(this, new MessageEventArgs(message));
        }

        protected void ThrowIfClosed()
        {
            if (IsClosed)
            {
                throw new InvalidOperationException(Strings.Plugin_ConnectionIsClosed);
            }
        }

        protected void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
    }

    public class MessageTracker
    {
        // Copypasta'd from some test
        private class ReferenceEqualityComparer<T> : IEqualityComparer, IEqualityComparer<T>
        {
            public bool Equals(T x, T y) => Equals((object)x, y);

            public int GetHashCode(T obj) => GetHashCode((object)obj);

            bool IEqualityComparer.Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private class MessageHolder
        {
            public JObject Props { get; } = new JObject();
            public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

        }
        ConcurrentDictionary<Message, string> MessageToLine = new ConcurrentDictionary<Message, string>(new ReferenceEqualityComparer<Message>());
        ConcurrentDictionary<string, MessageHolder> LineToProps = new ConcurrentDictionary<string, MessageHolder>(new ReferenceEqualityComparer<string>());
       
        public void AddMessageLine(string line)
        {
            if (!LineToProps.TryAdd(line, new MessageHolder()))
            {
                loggerThread.Enqueue(new{Warning=$"Duplicate AddMessageLine"});
            }
        }
        public void MapLineToMessage(string line, Message message)
        {
            if (!MessageToLine.TryAdd(message, line))
            {
                loggerThread.Enqueue(new{Warning="Duplicate MapLineToMessage"});
            }
        }

        public void MarkLine(string line, string property, object value)
        {
            if (!LineToProps.TryGetValue(line, out var holder))
            {
                loggerThread.Enqueue(new { Warning = "Line not found in MarkLine" });
                return;
            }

            holder.Props.Add($"{holder.Props.Count}-{property}", JToken.FromObject(value));
        }

        public void MarkMessage(Message message, string property, object value)
        {
            if (!MessageToLine.TryGetValue(message, out var line))
            {
                loggerThread.Enqueue(new { Warning = "Message not found in MarkMessage" });
                return;
            }

            MarkLine(line, property, value);
        }
        public void MarkLineTimestamp(string line, string property)
        {
            if (!LineToProps.TryGetValue(line, out var holder))
            {
                loggerThread.Enqueue(new { Warning = "Line not found in MarkLineTimestamp" });
                return;
            }

            holder.Props.Add(property, JToken.FromObject(holder.Stopwatch.Elapsed));
        }

        public void MarkMessageTimestamp(Message message, string property)
        {
            if (!MessageToLine.TryGetValue(message, out var line))
            {
                loggerThread.Enqueue(new { Warning = "Message not found in MarkMessageTimestamp" });
                return;
            }

            MarkLineTimestamp(line, property);
        }

        public void CompleteLine(string line)
        {
            if (!LineToProps.TryGetValue(line, out var holder))
            {
                loggerThread.Enqueue(new { Warning = "Line not found in CompleteLine" });
                return;
            }

            loggerThread.Enqueue(holder.Props);
            LineToProps.TryRemove(line, out _);
            foreach (var keyValuePair in MessageToLine.Where(x => object.ReferenceEquals(x.Value, line)))
            {
                MessageToLine.TryRemove(keyValuePair.Key, out _);
            }
        }

        public static MessageTracker Instance { get; } = new MessageTracker();
        AsyncLoggerThread loggerThread = new AsyncLoggerThread("messageTracker");


        public void Dispose()
        {
            loggerThread.Dispose();
        }
    }
}