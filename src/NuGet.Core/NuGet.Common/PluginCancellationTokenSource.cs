using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Common
{
    public static class PluginCancellationTokenSource
    {
        private static readonly ConcurrentDictionary<CancellationToken, ICancellationTokenHolder> AllTokens =
            new ConcurrentDictionary<CancellationToken, ICancellationTokenHolder>(
                new[]
                {
                    new KeyValuePair<CancellationToken, ICancellationTokenHolder>(
                        CancellationToken.None,
                        new ExternalCancellationTokenHolder(CancellationToken.None, "CancellationToken.None"))
                });

        public static ICancellationTokenSource CreateLinkedTokenSource(CancellationToken linkedToken, string name)
        {
            var newSource = CreateCore(
                name,
                CancellationTokenSource.CreateLinkedTokenSource(linkedToken),
                new[] {GetHolderForToken(linkedToken)});

            return newSource;
        }

        private static CancellationTokenSourceAdaptor CreateCore(
            string name,
            CancellationTokenSource cancellationTokenSource,
            IEnumerable<ICancellationTokenHolder> links)
        {
            var newSource = new CancellationTokenSourceAdaptor(cancellationTokenSource, name, links);
            newSource.Token.Register(OnCancel, newSource);
            if (!AllTokens.TryAdd(newSource.Token, newSource))
            {
                throw new Exception("Re-adding existing token?");
            }

            return newSource;
        }

        private static void OnCancel(object obj)
        {
            try
            {
                var holder = (ICancellationTokenHolder)obj;

                var sb = new StringBuilder();
                sb.AppendLine("Cancellation:");
                holder.Dump(sb);

                Debug.WriteLine(sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Caught exception while displaying cancellation info: {ex}");
            }
        }

        public static ICancellationTokenSource CreateWithTimeout(TimeSpan timeout, string name)
        {
            return CreateCore(name, new CancellationTokenSource(timeout), Enumerable.Empty<ICancellationTokenHolder>());
        }

        public static ICancellationTokenSource Create(string name)
        {
            return CreateCore(name, new CancellationTokenSource(), Enumerable.Empty<ICancellationTokenHolder>());
        }

        public static ICancellationTokenHolder GetHolderForToken(CancellationToken token)
        {
            return AllTokens.GetOrAdd(token, key => new ExternalCancellationTokenHolder(key));
        }
    }

    public class ExternalCancellationTokenHolder : ICancellationTokenHolder
    {
        public ExternalCancellationTokenHolder(CancellationToken token, string name)
        {
            Name = name;
            Token = token;
        }

        public ExternalCancellationTokenHolder(CancellationToken token)
            :this(token, $"(anonymous, hash={token.GetHashCode()})")
        {
        }

        public CancellationToken Token { get; }
        public string Name { get; }
        public IEnumerable<ICancellationTokenHolder> Links => Enumerable.Empty<ICancellationTokenHolder>();
        public void Dump(StringBuilder sb, string indent = "", string indentWith = "    ")
        {
            sb.AppendLine($"{indent}{this}");
        }

        public override string ToString()
        {
            return $"{(Token.IsCancellationRequested ? "Canceled" : "Not canceled")} [{Name}] {(Token.CanBeCanceled? "Can be canceled" : "Cannot be canceled")}";
        }
    }

    public class CancellationTokenSourceAdaptor : ICancellationTokenSource
    {
        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly CancellationToken tokenCopy;

        public CancellationTokenSourceAdaptor(CancellationTokenSource cancellationTokenSource, string name, IEnumerable<ICancellationTokenHolder> links)
        {
            this.cancellationTokenSource = cancellationTokenSource;
            tokenCopy = cancellationTokenSource.Token;
            Name = name;
            Links = new List<ICancellationTokenHolder>(links);
        }

        public CancellationToken Token => cancellationTokenSource.Token;
        public string Name { get; }
        public IEnumerable<ICancellationTokenHolder> Links { get; }
        public void Dump(StringBuilder sb, string indent = "", string indentWith = "    ")
        {
            sb.AppendLine($"{indent}{this}");
            indent += indentWith;
            foreach (var cancellationTokenHolder in Links)
            {
                cancellationTokenHolder.Dump(sb, indent, indentWith);
            }
        }

        public void Dispose()
        {
            cancellationTokenSource.Dispose();
        }

        public void Cancel(string reason)
        {
            CancelReason = CancelReason != null ? $"{CancelReason} / {reason}" : reason;
            CancelCallStack = CancelCallStack != null ? $"{Environment.StackTrace}\n---- Previous cancelation stack ----\n{CancelCallStack}" : Environment.StackTrace;
            cancellationTokenSource.Cancel();
        }

        public string CancelCallStack { get; private set; }

        public string CancelReason { get; private set; }

        public override string ToString()
        {
            return $"{(tokenCopy.IsCancellationRequested ? "Canceled" : "Not canceled")} [{Name}] {CancelReason}";
        }
    }

    public interface ICancellationTokenHolder
    {
        CancellationToken Token { get; }

        string Name { get; }

        IEnumerable<ICancellationTokenHolder> Links { get; }

        void Dump(StringBuilder sb, string indent = "", string indentWith = "    ");
    }

    public interface ICancellationTokenSource : ICancellationTokenHolder, IDisposable
    {
        void Cancel(string reason);
    }

    public static class NuGetTaskCompletionSource
    {
        public static ConcurrentDictionary<Task, ITaskCompletionSourceHolder> All = new ConcurrentDictionary<Task, ITaskCompletionSourceHolder>();
        public static ConcurrentDictionary<CancellationToken, ConcurrentBag<ITaskCompletionSourceHolder>> All2 = new ConcurrentDictionary<CancellationToken, ConcurrentBag<ITaskCompletionSourceHolder>>();

        public static ITaskCompletionSource<TResult> Create<TResult>(string name)
        {
            var adaptor = new TaskCompletionSourceAdaptor<TResult>(new TaskCompletionSource<TResult>(), name);
            adaptor.Task.ContinueWith(OnCancel, adaptor, TaskContinuationOptions.OnlyOnCanceled);
            All.TryAdd(adaptor.Task, adaptor);
            return adaptor;
        }

        private static void OnCancel<TResult>(Task<TResult> task, object state)
        {
            try
            {
                var holder = (ITaskCompletionSourceHolder)state;

                var sb = new StringBuilder();
                sb.AppendLine("Task Cancellation:");
                holder.Dump(sb);

                Debug.WriteLine(sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Caught exception while displaying cancellation info: {ex}");
            }
        }

        public static void AssociateTcsWithCancellationToken(ITaskCompletionSourceHolder taskCompletionSource, CancellationToken cancellationToken)
        {
            var list = All2.GetOrAdd(cancellationToken, _ => new ConcurrentBag<ITaskCompletionSourceHolder>());
            list.Add(taskCompletionSource);
        }
    }

    public interface ITaskCompletionSourceHolder
    {
        string Name { get; }

        void Dump(StringBuilder sb, string indent = "", string indentWith = "    ");
    }

    internal class TaskCompletionSourceAdaptor<TResult> : ITaskCompletionSource<TResult>
    {
        ConcurrentQueue<string> actions = new ConcurrentQueue<string>();
        Stopwatch stopwatch = Stopwatch.StartNew();
        public string Name { get; }
        public void Dump(StringBuilder sb, string indent = "", string indentWith = "    ")
        {
            sb.AppendLine($"{indent}{this}");
        }

        public override string ToString()
        {
            return $"{capturedTask.Status} [{Name}] {CancelReason} {string.Join(" -> ", actions)}";
        }

        private readonly TaskCompletionSource<TResult> innerTcs;
        private readonly Task<TResult> capturedTask;

        public TaskCompletionSourceAdaptor(TaskCompletionSource<TResult> innerTcs, string name)
        {
            Name = name;
            this.innerTcs = innerTcs;
            capturedTask = innerTcs.Task;
        }

        public Task<TResult> Task => innerTcs.Task;
        public bool TrySetCanceled(string reason)
        {
            CancelReason = CancelReason != null ? $"{CancelReason} / {reason}" : reason;
            CancelCallStack = CancelCallStack != null ? $"{Environment.StackTrace}\n---- Previous cancelation stack ----\n{CancelCallStack}" : Environment.StackTrace;
            actions.Enqueue($"{stopwatch.Elapsed}:TrySetCanceled");
            return innerTcs.TrySetCanceled();
        }

        public bool TrySetResult(TResult result)
        {
            actions.Enqueue($"{stopwatch.Elapsed}:TrySetResult");
            return innerTcs.TrySetResult(result);
        }

        public string CancelCallStack { get; private set; }

        public string CancelReason { get; private set; }
    }

    public interface ITaskCompletionSource<TResult> : ITaskCompletionSourceHolder
    {
        Task<TResult> Task { get; }
        bool TrySetCanceled(string reason);
        bool TrySetResult(TResult result);
    }
}
