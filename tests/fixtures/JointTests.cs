using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class JointTests
{
    static Rigidbody Box(Vector3 p, Vector3 size)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.transform.position = p; g.transform.localScale = size;
        return g.AddComponent<Rigidbody>();
    }
    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    [UnityTest] public IEnumerator FixedJointToWorldHolds()
    {
        var rb = Box(new Vector3(0, 5, 0), Vector3.one);
        rb.gameObject.AddComponent<FixedJoint>();
        yield return Fixed(100);
        Assert.AreEqual(5f, rb.position.y, 0.01f);
    }

    [UnityTest] public IEnumerator PendulumHingeSwingsAroundAnchor()
    {
        var rb = Box(new Vector3(2, 5, 0), new Vector3(0.2f, 0.2f, 0.2f));
        var h = rb.gameObject.AddComponent<HingeJoint>();
        h.anchor = new Vector3(-10, 0, 0);          // local, scaled by 0.2 → world (0,5,0)
        h.axis = Vector3.forward;
        float minX = 10;
        for (int i = 0; i < 120; i++) { yield return new WaitForFixedUpdate(); minX = Mathf.Min(minX, rb.position.x); Assert.AreEqual(2f, (rb.position - new Vector3(0, 5, 0)).magnitude, 0.05f, "rope length kept"); }
        Assert.Less(minX, -1.5f, "swings through to the other side");
    }

    [UnityTest] public IEnumerator HingeLimitsAndMotor()
    {
        var door = Box(new Vector3(0.5f, 1, 0), new Vector3(1, 2, 0.1f));
        door.useGravity = false;
        var h = door.gameObject.AddComponent<HingeJoint>();
        h.anchor = new Vector3(-0.5f, 0, 0); h.axis = Vector3.up;
        h.useLimits = true; h.limits = new JointLimits { min = -10, max = 90 };
        h.useMotor = true; h.motor = new JointMotor { targetVelocity = 180, force = 50 };
        yield return Fixed(100);
        Assert.AreEqual(90f, Mathf.Abs(h.angle), 3f, "stops at the 90° limit");
    }

    [UnityTest] public IEnumerator ChainOfSpheresHangs()
    {
        Rigidbody prev = null;
        var first = new GameObject("Anchor"); first.transform.position = new Vector3(0, 10, 0);
        var arb = first.AddComponent<Rigidbody>(); arb.isKinematic = true; prev = arb;
        Rigidbody last = null;
        for (int i = 1; i <= 5; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.transform.localScale = Vector3.one * 0.5f;
            g.transform.position = new Vector3(i * 1f, 10, 0);
            var rb = g.AddComponent<Rigidbody>(); rb.drag = 1; rb.angularDrag = 1;
            var j = g.AddComponent<ConfigurableJoint>(); j.connectedBody = prev; j.anchor = new Vector3(-2, 0, 0);
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;   // ball and socket
            prev = rb; last = rb;
        }
        yield return Fixed(400);
        Assert.AreEqual(0f, last.position.x, 0.3f, "chain hangs straight down");
        Assert.AreEqual(5f, last.position.y, 0.4f);
    }

    class BreakRecorder : MonoBehaviour { public float force = -1; void OnJointBreak(float f) => force = f; }

    [UnityTest] public IEnumerator JointBreaksUnderLoad()
    {
        var rb = Box(new Vector3(0, 5, 0), Vector3.one); rb.mass = 10;
        var j = rb.gameObject.AddComponent<FixedJoint>(); j.breakForce = 50;   // weight 98 N > 50 N
        var rec = rb.gameObject.AddComponent<BreakRecorder>();
        yield return Fixed(5);
        Assert.Greater(rec.force, 50f, "OnJointBreak with the breaking force");
        Assert.IsTrue(rb.GetComponent<FixedJoint>() == null, "joint destroyed");
        yield return Fixed(20);
        Assert.Less(rb.position.y, 4.5f, "falls after breaking");
    }

    [UnityTest] public IEnumerator SpringJointOscillates()
    {
        var rb = Box(new Vector3(0, 5, 0), Vector3.one * 0.3f);
        var s = rb.gameObject.AddComponent<SpringJoint>();
        s.autoConfigureConnectedAnchor = false; s.connectedAnchor = new Vector3(0, 6, 0); s.spring = 50; s.damper = 5f;
        float minY = 99;
        for (int i = 0; i < 400; i++) { yield return new WaitForFixedUpdate(); minY = Mathf.Min(minY, rb.position.y); }
        // equilibrium: k·x = m·g → x = 9.81 / 50 ≈ 0.2 below the rest length (0) → y ≈ 6 − 0.2
        Assert.Less(minY, 5.9f);
        Assert.AreEqual(6 - 9.81f / 50f, rb.position.y, 0.15f);
    }
}
