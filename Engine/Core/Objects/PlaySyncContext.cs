// Play mode's main-thread SynchronizationContext, as Unity's UnitySynchronizationContext: continuations of awaited Tasks
// (DOTween's AsyncWaitForCompletion, Task.Yield, async void handlers) are queued and run on the main thread once per frame
// at Update.ScriptRunDelayedTasks, instead of on the thread pool where they would race the player loop.
using System;
using System.Collections.Generic;
using System.Threading;

namespace MiniUnity
{
    sealed class PlaySyncContext : SynchronizationContext
    {
        readonly object m_Lock = new();
        List<(SendOrPostCallback cb, object state)> m_Queue = new(), m_Running = new();
        readonly int m_Thread = Environment.CurrentManagedThreadId;

        public override void Post(SendOrPostCallback d, object state) { lock (m_Lock) m_Queue.Add((d, state)); }
        public override void Send(SendOrPostCallback d, object state)
        {
            if (Environment.CurrentManagedThreadId == m_Thread) { d(state); return; }
            using var done = new ManualResetEventSlim();
            Post(s => { try { d(s); } finally { done.Set(); } }, state);
            done.Wait();
        }
        public override SynchronizationContext CreateCopy() => this;

        /// <summary>Runs the callbacks posted before this call; ones they post run next frame (Unity's ExecuteTasks).</summary>
        public void Exec()
        {
            lock (m_Lock) (m_Queue, m_Running) = (m_Running, m_Queue);
            foreach (var (cb, state) in m_Running)
            {
                try { cb(state); }
                catch (Exception e) { Play.Report(e, "SynchronizationContext"); }
            }
            m_Running.Clear();
        }
    }

    public static partial class Play
    {
        static PlaySyncContext s_Sync;
        static SynchronizationContext s_PrevSync;

        static void InstallSyncContext()
        {
            if (s_Sync == null) s_PrevSync = SynchronizationContext.Current;
            s_Sync = new PlaySyncContext();
            SynchronizationContext.SetSynchronizationContext(s_Sync);
        }

        static void RemoveSyncContext()
        {
            if (s_Sync == null) return;
            if (SynchronizationContext.Current == s_Sync) SynchronizationContext.SetSynchronizationContext(s_PrevSync);
            s_Sync = null; s_PrevSync = null;
        }
    }
}
