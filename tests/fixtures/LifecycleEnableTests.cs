using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.TestTools;

// OnDisable only follows an OnEnable: a behaviour that turns itself off in Awake (QuickOutline's Outline does) gets neither.
public class LifecycleEnableTests
{
    class SelfDisabling : MonoBehaviour
    {
        public int Enables, Disables;
        void Awake() { enabled = false; }
        void OnEnable() => Enables++;
        void OnDisable() => Disables++;
    }

    [UnityTest] public IEnumerator DisabledInAwakeGetsNoOnDisable()
    {
        var b = new GameObject("Outline").AddComponent<SelfDisabling>();
        Assert.AreEqual(0, b.Enables); Assert.AreEqual(0, b.Disables, "never enabled, so never disabled");
        yield return null;
        b.enabled = true;
        Assert.AreEqual(1, b.Enables);
        b.enabled = false;
        Assert.AreEqual(1, b.Disables);
        Object.Destroy(b.gameObject);
        yield return null;
        Assert.AreEqual(1, b.Disables, "destroying a disabled behaviour sends no second OnDisable");
    }

    class ThrowsOnEnable : MonoBehaviour { void OnEnable() => throw new System.InvalidOperationException("boom in OnEnable"); }

    [UnityTest] public IEnumerator ExceptionInOnEnableIsLoggedNotThrown()
    {
        var go = new GameObject("Panel"); go.SetActive(false);
        go.AddComponent<ThrowsOnEnable>();
        LogAssert.Expect(LogType.Exception, new Regex("boom in OnEnable"));
        go.SetActive(true);   // Unity logs the exception; the caller (a UI panel's Show) carries on
        Assert.IsTrue(go.activeSelf, "SetActive returned normally");
        yield return null;
    }

    [Test] public void SubEmittersModuleReadsAsEmpty()
    {
        var ps = new GameObject("PS").AddComponent<ParticleSystem>();
        var sub = ps.subEmitters;
        Assert.IsFalse(sub.enabled);
        Assert.AreEqual(0, sub.subEmittersCount);
    }

    [Test] public void FormerlySerializedAsKeepsItsName()
    {
        Assert.AreEqual("_speed", new FormerlySerializedAsAttribute("_speed").oldName);
    }
}
