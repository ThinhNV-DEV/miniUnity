using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class Physics2DMoreTests
{
    static GameObject Ground(float y = 0)
    {
        var g = new GameObject("Ground"); g.transform.position = new Vector3(0, y - 0.5f, 0);
        g.AddComponent<BoxCollider2D>().size = new Vector2(80, 1);
        return g;
    }
    static Rigidbody2D Body(string name, Vector2 p) { var g = new GameObject(name); g.transform.position = p; return g.AddComponent<Rigidbody2D>(); }
    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    [UnityTest] public IEnumerator CapsulesRestUprightAndRollEndToEnd()
    {
        Ground();
        var a = Body("CapV", new Vector2(0, 3)); a.gameObject.AddComponent<CapsuleCollider2D>().size = new Vector2(0.5f, 2);
        a.freezeRotation = true;
        var b = Body("CapH", new Vector2(5, 1)); var hc = b.gameObject.AddComponent<CapsuleCollider2D>();
        hc.size = new Vector2(2, 0.5f); hc.direction = CapsuleDirection2D.Horizontal;
        yield return Fixed(120);
        Assert.AreEqual(1, a.position.y, 0.03f, "vertical capsule rests on its rounded end");
        Assert.AreEqual(0.25f, b.position.y, 0.03f, "horizontal capsule lies on its side");
        Assert.AreEqual(0, b.rotation, 0.5f);
    }

    [UnityTest] public IEnumerator CompositeColliderMergesChildren()
    {
        Ground();
        var root = Body("Composite", new Vector2(0, 3));
        var comp = root.gameObject.AddComponent<CompositeCollider2D>();
        for (int i = 0; i < 3; i++)
        {
            var c = new GameObject("Part" + i); c.transform.SetParent(root.transform, false); c.transform.localPosition = new Vector3(i - 1, 0, 0);
            c.AddComponent<BoxCollider2D>().usedByComposite = true;
        }
        yield return Fixed(120);
        Assert.AreEqual(0.5f, root.position.y, 0.03f, "three merged boxes rest flat");
        var hit = Physics2D.Raycast(new Vector2(1, 5), Vector2.down);
        Assert.AreEqual(comp, hit.collider, "queries report the composite collider");
        Assert.AreEqual(1, root.attachedColliderCount, "children used by the composite are not separate colliders");
    }

    [UnityTest] public IEnumerator AreaEffectorPushesBodiesInside()
    {
        var zone = new GameObject("Wind"); zone.transform.position = new Vector3(0, 0, 0);
        var zc = zone.AddComponent<BoxCollider2D>(); zc.size = new Vector2(10, 10); zc.isTrigger = true; zc.usedByEffector = true;
        var eff = zone.AddComponent<AreaEffector2D>(); eff.forceAngle = 90; eff.forceMagnitude = 20; eff.useGlobalAngle = true;
        var rb = Body("Leaf", new Vector2(0, 0)); rb.gameObject.AddComponent<CircleCollider2D>().radius = 0.2f;
        yield return Fixed(20);
        Assert.Greater(rb.velocity.y, 0, "upward force beats gravity (20 N > 9.81 N)");
    }

    [UnityTest] public IEnumerator SliderJointMotorAndLimits()
    {
        var rb = Body("Slider", new Vector2(0, 5)); rb.gameObject.AddComponent<BoxCollider2D>(); rb.gravityScale = 0;
        var s = rb.gameObject.AddComponent<SliderJoint2D>();
        s.autoConfigureAngle = false; s.angle = 0; s.useMotor = true; s.motor = new JointMotor2D { motorSpeed = 2, maxMotorTorque = 1000 };
        s.useLimits = true; s.limits = new JointTranslationLimits2D { min = -1, max = 3 };
        yield return Fixed(25);
        Assert.AreEqual(2, rb.velocity.x, 0.05f, "motor speed along the axis");
        Assert.AreEqual(5, rb.position.y, 1e-3f, "locked perpendicular");
        yield return Fixed(100);
        Assert.AreEqual(3, s.jointTranslation, 0.02f, "upper limit");
        Assert.AreEqual(JointLimitState2D.UpperLimit, s.limitState);
    }

    [UnityTest] public IEnumerator WheelJointSuspensionSags()
    {
        Ground();
        var car = Body("Car", new Vector2(0, 2)); car.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(3, 0.5f);
        var wheels = new List<WheelJoint2D>();
        foreach (var x in new[] { -1f, 1f })
        {
            var w = Body("Wheel", new Vector2(x, 1.2f)); w.gameObject.AddComponent<CircleCollider2D>().radius = 0.4f;
            var j = car.gameObject.AddComponent<WheelJoint2D>(); j.connectedBody = w; j.anchor = new Vector2(x, -0.8f);
            wheels.Add(j);
        }
        yield return Fixed(150);
        Assert.Less(car.position.y, 2f, "suspension compressed under the chassis weight");
        Assert.Greater(car.position.y, 1.0f);
        Assert.AreEqual(0, car.rotation, 1f);
    }

    [UnityTest] public IEnumerator TargetJointPullsTowardTarget()
    {
        var rb = Body("Grab", new Vector2(0, 0)); rb.gameObject.AddComponent<CircleCollider2D>(); rb.gravityScale = 0;
        var t = rb.gameObject.AddComponent<TargetJoint2D>();
        t.target = new Vector2(3, 2);
        yield return Fixed(100);
        Assert.AreEqual(3, rb.position.x, 0.05f); Assert.AreEqual(2, rb.position.y, 0.05f);
    }

    [UnityTest] public IEnumerator FixedJointWeldsAndBreaks()
    {
        var a = Body("A", new Vector2(0, 5)); a.gameObject.AddComponent<BoxCollider2D>(); a.bodyType = RigidbodyType2D.Static;
        var b = Body("B", new Vector2(1, 5)); b.gameObject.AddComponent<BoxCollider2D>();
        var f = b.gameObject.AddComponent<FixedJoint2D>(); f.connectedBody = a;
        yield return Fixed(50);
        Assert.AreEqual(1, b.position.x, 0.02f); Assert.AreEqual(5, b.position.y, 0.02f, "welded cantilever holds");
        Assert.AreEqual(0, b.rotation, 0.5f);
        var rec = b.gameObject.AddComponent<BreakRecorder>();
        f.breakForce = 5;   // weight is 9.81 N
        yield return Fixed(5);
        Assert.IsTrue(rec.broken, "OnJointBreak2D");
        Assert.IsTrue(f == null, "joint destroyed (breakAction Destroy)");
        yield return Fixed(30);
        Assert.Less(b.position.y, 4.5f, "falls after breaking");
    }

    class BreakRecorder : MonoBehaviour { public bool broken; void OnJointBreak2D(Joint2D j) => broken = true; }

    [UnityTest] public IEnumerator ContactsAndDistanceQueries()
    {
        Ground();
        var rb = Body("Box", new Vector2(0, 0.6f)); var bc = rb.gameObject.AddComponent<BoxCollider2D>();
        var far = new GameObject("Far"); far.transform.position = new Vector3(5, 0.5f, 0); var fc = far.AddComponent<CircleCollider2D>();
        yield return Fixed(30);
        var pts = new List<ContactPoint2D>();
        Assert.AreEqual(2, bc.GetContacts(pts), "two manifold points for a box face contact");
        foreach (var p in pts) { Assert.AreEqual(1, p.normal.y, 1e-3f); Assert.Greater(p.normalImpulse, 0); Assert.AreEqual(bc, p.otherCollider); }
        var cols = new Collider2D[4];
        Assert.AreEqual(1, rb.GetContacts(cols)); Assert.AreEqual("Ground", cols[0].name);
        Assert.IsTrue(bc.IsTouchingLayers());
        var d = bc.Distance(fc);
        Assert.IsTrue(d.isValid); Assert.IsFalse(d.isOverlapped);
        Assert.AreEqual(5 - 0.5f - 0.5f, d.distance, 0.05f, "box face to circle surface");
        Assert.AreEqual(1, d.normal.x, 0.05f);
        var filter = new ContactFilter2D(); filter.SetNormalAngle(80, 100);   // upward normals only
        Assert.AreEqual(2, bc.GetContacts(filter, pts));
        filter.SetNormalAngle(-10, 10);
        Assert.AreEqual(0, bc.GetContacts(filter, pts));
    }

    class StayCounter : MonoBehaviour { public int stays; void OnCollisionStay2D(Collision2D c) => stays++; }

    [UnityTest] public IEnumerator NoStayCallbacksWhileAsleep()
    {
        Ground();
        var rb = Body("Box", new Vector2(0, 0.6f)); rb.gameObject.AddComponent<BoxCollider2D>();
        var sc = rb.gameObject.AddComponent<StayCounter>();
        yield return Fixed(100);
        Assert.IsTrue(rb.IsSleeping());
        int n = sc.stays;
        yield return Fixed(20);
        Assert.AreEqual(n, sc.stays, "sleeping bodies don't get OnCollisionStay2D");
        rb.WakeUp();
        yield return Fixed(2);
        Assert.Greater(sc.stays, n, "awake again → stay resumes");
    }

    [UnityTest] public IEnumerator ManyBodiesStepQuickly()
    {
        Ground();
        for (int i = 0; i < 200; i++)
        {
            var rb = Body("B" + i, new Vector2((i % 20) - 10, 1 + (i / 20) * 1.1f));
            if (i % 2 == 0) rb.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(0.9f, 0.9f);
            else rb.gameObject.AddComponent<CircleCollider2D>().radius = 0.45f;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        yield return Fixed(100);
        Assert.Less(sw.ElapsedMilliseconds, 15000, "200 bodies × 100 steps");
        Debug.Log($"200 bodies x 100 steps: {sw.ElapsedMilliseconds} ms");
    }
}
