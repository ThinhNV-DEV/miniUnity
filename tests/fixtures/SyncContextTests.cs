using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Play mode runs awaited Task continuations on the main thread once per frame (UnitySynchronizationContext), so async code
// such as DOTween's AsyncWaitForCompletion loops never touches the scene from a thread-pool thread.
public class SyncContextTests
{
    class AsyncUser : MonoBehaviour
    {
        public int MainThread, ResumedThread = -1, StartFrame, ResumedFrame = -1, Yields;

        public async void Run()
        {
            MainThread = Thread.CurrentThread.ManagedThreadId; StartFrame = Time.frameCount;
            await Task.Yield();
            ResumedThread = Thread.CurrentThread.ManagedThreadId; ResumedFrame = Time.frameCount;
            while (Yields < 5) { Yields++; await Task.Yield(); }
        }
    }

    [UnityTest] public IEnumerator TaskContinuationsRunOnTheMainThreadEachFrame()
    {
        Assert.IsNotNull(SynchronizationContext.Current, "play mode installs a SynchronizationContext");
        var u = new GameObject("Async").AddComponent<AsyncUser>();
        u.Run();
        Assert.AreEqual(-1, u.ResumedThread, "Task.Yield resumes later, not inline");
        yield return null;
        Assert.AreEqual(u.MainThread, u.ResumedThread, "continuation on the main thread");
        Assert.Greater(u.ResumedFrame, u.StartFrame);
        int yields = u.Yields;
        yield return null;
        Assert.AreEqual(yields + 1, u.Yields, "one queued continuation per frame");
    }
}
