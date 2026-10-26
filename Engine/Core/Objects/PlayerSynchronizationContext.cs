// Play-mode main-thread SynchronizationContext, as Unity's UnitySynchronizationContext: Post queues the callback and the queue runs
// once per frame in Update.ScriptRunDelayedTasks, after coroutines. TaskScheduler.FromCurrentSynchronizationContext() throws without
// one (Unity Services Core and IAP capture it at startup), and an `await` continuation that expects the main thread needs it.
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace MiniUnity
{
    public sealed class PlayerSynchronizationContext : SynchronizationContext
    {
        readonly ConcurrentQueue<(SendOrPostCallback cb, object state)> m_Queue = new();
        readonly int m_Thread = Environment.CurrentManagedThreadId;

        public static PlayerSynchronizationContext Instance { get; private set; }

        /// <summary>Install a fresh context on this (the main) thread: work queued by an earlier session is dropped.</summary>
        public static void Install() { Instance = new PlayerSynchronizationContext(); SetSynchronizationContext(Instance); }

        public override void Post(SendOrPostCallback d, object state) => m_Queue.Enqueue((d, state));
        public override void Send(SendOrPostCallback d, object state)
        {
            if (Environment.CurrentManagedThreadId == m_Thread) d(state);
            else { using var done = new ManualResetEventSlim(); Post(s => { try { d(s); } finally { done.Set(); } }, state); done.Wait(); }
        }
        public override SynchronizationContext CreateCopy() => this;

        /// <summary>Run what was queued before this call (work queued while running waits for the next frame, as in Unity).</summary>
        public static void ExecuteTasks()
        {
            var ctx = Instance;
            if (ctx == null) return;
            for (int n = ctx.m_Queue.Count; n > 0 && ctx.m_Queue.TryDequeue(out var w); n--)
            {
                try { w.cb(w.state); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
