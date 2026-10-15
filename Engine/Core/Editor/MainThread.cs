// Editor main-thread SynchronizationContext: continuations of async editor code (async void menu handlers,
// awaited Tasks) are queued and run on this thread, like Unity's UnitySynchronizationContext. Run() pumps until
// the started work and every async void operation have finished.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MiniUnity
{
    public sealed class MainThread : SynchronizationContext
    {
        readonly BlockingCollection<(SendOrPostCallback cb, object state)> m_Queue = new();
        readonly int m_Thread = Environment.CurrentManagedThreadId;
        int m_Ops;

        public override void Post(SendOrPostCallback d, object state) => m_Queue.Add((d, state));
        public override void Send(SendOrPostCallback d, object state)
        {
            if (Environment.CurrentManagedThreadId == m_Thread) d(state);
            else { using var done = new ManualResetEventSlim(); Post(s => { d(s); done.Set(); }, state); done.Wait(); }
        }
        public override void OperationStarted() => Interlocked.Increment(ref m_Ops);
        public override void OperationCompleted() => Interlocked.Decrement(ref m_Ops);
        public override SynchronizationContext CreateCopy() => this;

        /// <summary>Run <paramref name="start"/> with this context installed and pump until all queued work is done.</summary>
        public static object Run(Func<object> start)
        {
            var prev = Current;
            var ctx = new MainThread();
            SetSynchronizationContext(ctx);
            try
            {
                var r = start();
                var task = r as Task;
                while (true)
                {
                    if (ctx.m_Queue.TryTake(out var item, 50)) { item.cb(item.state); continue; }
                    if ((task == null || task.IsCompleted) && Volatile.Read(ref ctx.m_Ops) <= 0 && ctx.m_Queue.Count == 0) break;
                }
                task?.GetAwaiter().GetResult();
                return r;
            }
            finally { SetSynchronizationContext(prev); }
        }
    }
}
