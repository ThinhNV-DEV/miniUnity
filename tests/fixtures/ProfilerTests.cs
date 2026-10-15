using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.Profiling;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

// Unity's NUnit constraints (built against Unity's nunit.framework) running on MiniUnity's NUnit + the GC.Alloc recorder.
public class ProfilerTests
{
    [Test] public void NotAllocatingGCMemoryPassesForArithmetic()
    {
        int x = 0;
        Assert.That(() => { for (int i = 0; i < 100; i++) x += i; }, Is.Not.AllocatingGCMemory());
        Assert.AreEqual(4950, x);
    }

    [Test] public void AllocatingGCMemoryDetectsAnAllocation()
    {
        List<int> keep = null;
        Assert.That(() => { keep = new List<int>(16); }, Is.AllocatingGCMemory());
        Assert.IsNotNull(keep);
        Assert.Throws<AssertionException>(() => Assert.That(() => { keep = new List<int>(4); }, Is.Not.AllocatingGCMemory()));
    }

    [Test] public void RecorderMeasuresTheEnabledInterval()
    {
        var r = Recorder.Get("GC.Alloc");
        Assert.IsTrue(r.isValid);
        r.enabled = false; r.FilterToCurrentThread(); r.enabled = true;
        var a = new int[64];
        r.enabled = false; r.CollectFromAllThreads();
        Assert.AreEqual(1, r.sampleBlockCount);
        Assert.AreEqual(64, a.Length);
        Assert.AreSame(r, Recorder.Get("GC.Alloc"));
    }
}
