using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class Physics2DTests
{
    static GameObject Ground(float y = 0, float angleZ = 0, PhysicsMaterial2D mat = null)
    {
        var g = new GameObject("Ground");
        g.transform.position = new Vector3(0, y - 0.5f, 0);
        g.transform.rotation = Quaternion.Euler(0, 0, angleZ);
        var c = g.AddComponent<BoxCollider2D>(); c.size = new Vector2(60, 1);
        if (mat != null) c.sharedMaterial = mat;
        return g;
    }

    static Rigidbody2D Ball(Vector2 p, float r = 0.5f)
    {
        var g = new GameObject("Ball");
        g.transform.position = p;
        g.AddComponent<CircleCollider2D>().radius = r;
        return g.AddComponent<Rigidbody2D>();
    }

    static Rigidbody2D Box(Vector2 p, Vector2 size, float mass = 1)
    {
        var g = new GameObject("Box");
        g.transform.position = p;
        g.AddComponent<BoxCollider2D>().size = size;
        var rb = g.AddComponent<Rigidbody2D>(); rb.mass = mass; return rb;
    }

    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    [UnityTest] public IEnumerator FreeFallMatchesBox2DIntegration()
    {
        var rb = Ball(new Vector2(0, 100));
        yield return Fixed(25);
        float dt = Time.fixedDeltaTime, g = Physics2D.gravity.y; int n = 25;
        Assert.AreEqual(100 + g * dt * dt * n * (n + 1) / 2, rb.position.y, 1e-3f, "position (v += g·dt; x += v·dt)");
        Assert.AreEqual(g * dt * n, rb.velocity.y, 1e-4f, "velocity");
    }

    [UnityTest] public IEnumerator LinearDragUsesBox2DDamping()
    {
        var rb = Ball(new Vector2(0, 100)); rb.gravityScale = 0; rb.drag = 2;
        rb.velocity = new Vector2(10, 0);
        yield return Fixed(10);
        float expect = 10 * Mathf.Pow(1f / (1 + 2 * Time.fixedDeltaTime), 10);   // v *= 1 / (1 + h·damping)
        Assert.AreEqual(expect, rb.velocity.x, 1e-3f);
    }

    [UnityTest] public IEnumerator GravityScaleAndKinematicVelocity()
    {
        var a = Ball(new Vector2(-5, 50)); a.gravityScale = 0.5f;
        var k = Ball(new Vector2(5, 50)); k.bodyType = RigidbodyType2D.Kinematic; k.velocity = new Vector2(1, 0);
        yield return Fixed(10);
        Assert.AreEqual(Physics2D.gravity.y * 0.5f * 10 * Time.fixedDeltaTime, a.velocity.y, 1e-4f);
        Assert.AreEqual(5 + 10 * Time.fixedDeltaTime, k.position.x, 1e-4f, "kinematic bodies move with their velocity");
        Assert.AreEqual(50, k.position.y, 1e-5f, "no gravity on kinematic bodies");
    }

    [UnityTest] public IEnumerator BoxRestsOnGroundWithSkinGapAndSleeps()
    {
        Ground();
        var rb = Box(new Vector2(0, 2), Vector2.one);
        yield return Fixed(150);
        // Box2D polygons carry a skin (polygon radius = default contact offset 0.01 each) → resting gap ≈ 0.02 minus linearSlop
        float bottom = rb.position.y - 0.5f;
        Assert.That(bottom, Is.InRange(0.005f, 0.025f), "box bottom sits on the skin");
        Assert.IsTrue(rb.IsSleeping(), "sleeps after timeToSleep with low velocity");
        Assert.AreEqual(0, rb.rotation, 0.01f);
    }

    [UnityTest] public IEnumerator StackOfBoxesStaysStable()
    {
        Ground();
        var boxes = new List<Rigidbody2D>();
        for (int i = 0; i < 8; i++) boxes.Add(Box(new Vector2(0, 0.5f + i * 1.02f), Vector2.one));
        yield return Fixed(300);
        foreach (var b in boxes) Assert.AreEqual(0, b.position.x, 0.05f, "no drift");
        Assert.Greater(boxes[7].position.y, 7.3f, "top box still on the stack");
        Assert.IsTrue(boxes[7].IsSleeping(), "stack sleeps as one island");
    }

    [UnityTest] public IEnumerator RestitutionUsesVelocityThreshold()
    {
        var bouncy = new PhysicsMaterial2D { bounciness = 1, friction = 0.4f };
        Ground();
        var rb = Ball(new Vector2(0, 5)); rb.GetComponent<Collider2D>().sharedMaterial = bouncy;
        float maxUp = 0, apex = 0; bool landed = false;
        for (int i = 0; i < 150; i++)
        {
            yield return new WaitForFixedUpdate();
            maxUp = Mathf.Max(maxUp, rb.velocity.y);
            if (rb.position.y < 0.6f) landed = true;
            if (landed) apex = Mathf.Max(apex, rb.position.y);
        }
        Assert.Greater(maxUp, 9f, "bounciness 1 (max mix) reflects the impact speed");
        Assert.Greater(apex, 4.5f, "rebounds close to the drop height");

        // below Physics2D.velocityThreshold there is no bounce
        var slow = Ball(new Vector2(10, 0.5f + 0.02f)); slow.GetComponent<Collider2D>().sharedMaterial = bouncy;
        slow.velocity = new Vector2(0, -0.5f);
        yield return Fixed(10);
        Assert.Less(slow.velocity.y, 0.3f, "slow impact does not bounce");
    }

    [UnityTest] public IEnumerator FrictionOnSlopeUsesGeometricMean()
    {
        // slope 30°, tan 30° ≈ 0.577: friction sqrt(1·1) = 1 holds, sqrt(0.1·0.1) = 0.1 slides
        var high = new PhysicsMaterial2D { friction = 1 }; var low = new PhysicsMaterial2D { friction = 0.1f };
        Ground(0, 30, high);
        var a = Box(new Vector2(0, -0.5f) + (Vector2)(Quaternion.Euler(0, 0, 30) * new Vector3(-3, 1.02f)), Vector2.one);
        a.rotation = 30; a.GetComponent<Collider2D>().sharedMaterial = high;
        var g2 = Ground(-20, 30, low);
        var b = Box(new Vector2(0, -20.5f) + (Vector2)(Quaternion.Euler(0, 0, 30) * new Vector3(0, 1.02f)), Vector2.one);
        b.rotation = 30; b.GetComponent<Collider2D>().sharedMaterial = low;
        yield return Fixed(5);
        var a0 = a.position; var b0 = b.position;
        yield return Fixed(50);
        Assert.Less((a.position - a0).magnitude, 0.02f, "high friction holds");
        float t0 = 5 * Time.fixedDeltaTime, t1 = 55 * Time.fixedDeltaTime, accel = 9.81f * (Mathf.Sin(30 * Mathf.Deg2Rad) - 0.1f * Mathf.Cos(30 * Mathf.Deg2Rad));
        Assert.AreEqual(0.5f * accel * (t1 * t1 - t0 * t0), (b.position - b0).magnitude, 0.12f, "low friction slides with g(sinθ − μcosθ)");
    }

    class Recorder : MonoBehaviour
    {
        public List<string> log = new List<string>();
        public Collision2D lastEnter;
        void OnCollisionEnter2D(Collision2D c) { log.Add("enter:" + c.collider.name); lastEnter = c; }
        void OnCollisionStay2D(Collision2D c) { if (!log.Contains("stay")) log.Add("stay"); }
        void OnCollisionExit2D(Collision2D c) => log.Add("exit:" + c.collider.name);
        void OnTriggerEnter2D(Collider2D c) => log.Add("tenter:" + c.name);
        void OnTriggerStay2D(Collider2D c) { if (!log.Contains("tstay")) log.Add("tstay"); }
        void OnTriggerExit2D(Collider2D c) => log.Add("texit:" + c.name);
    }

    [UnityTest] public IEnumerator CollisionCallbacksReportOtherColliderAndNormal()
    {
        Ground();
        var rb = Ball(new Vector2(0, 2));
        var rec = rb.gameObject.AddComponent<Recorder>();
        yield return Fixed(60);
        Assert.Contains("enter:Ground", rec.log);
        Assert.Contains("stay", rec.log);
        var c = rec.lastEnter;
        Assert.AreEqual("Ground", c.collider.name, "collider = the other collider");
        Assert.AreEqual(rb.GetComponent<Collider2D>(), c.otherCollider, "otherCollider = this collider");
        Assert.AreEqual(1, c.contactCount);
        Assert.AreEqual(0, c.GetContact(0).normal.x, 1e-3f);
        Assert.AreEqual(1, c.GetContact(0).normal.y, 1e-3f, "normal points from the ground towards the ball");
        Assert.Greater(c.relativeVelocity.y, 4f, "relative velocity = other − this at impact");
        rb.velocity = new Vector2(0, 20);
        yield return Fixed(10);
        Assert.Contains("exit:Ground", rec.log);
    }

    [UnityTest] public IEnumerator TriggersReportEnterStayExit()
    {
        var zone = new GameObject("Zone"); zone.transform.position = new Vector3(0, 5, 0);
        var zc = zone.AddComponent<BoxCollider2D>(); zc.size = new Vector2(4, 1); zc.isTrigger = true;
        var rb = Ball(new Vector2(0, 8), 0.25f);
        var rec = rb.gameObject.AddComponent<Recorder>();
        yield return Fixed(80);
        CollectionAssert.IsSubsetOf(new[] { "tenter:Zone", "tstay", "texit:Zone" }, rec.log);
        Assert.Less(rb.position.y, 4, "triggers don't stop the body");
        Assert.IsFalse(rec.log.Exists(s => s.StartsWith("enter")), "no collision callbacks for triggers");
    }

    [UnityTest] public IEnumerator RaycastsFollowUnityRules()
    {
        Ground();
        var box = Box(new Vector2(0, 3), Vector2.one); box.bodyType = RigidbodyType2D.Static;
        var tr = new GameObject("Trig"); tr.transform.position = new Vector3(0, 6, 0);
        var tc = tr.AddComponent<CircleCollider2D>(); tc.isTrigger = true;
        yield return null;
        var hit = Physics2D.Raycast(new Vector2(0, 10), Vector2.down);
        Assert.IsTrue(hit, "hit");
        Assert.AreEqual("Trig", hit.collider.name, "queriesHitTriggers = true by default");
        Assert.AreEqual(3.5f, hit.distance, 1e-3f);
        var f = new ContactFilter2D(); f.useTriggers = false;
        var res = new List<RaycastHit2D>();
        Assert.AreEqual(2, Physics2D.Raycast(new Vector2(0, 10), Vector2.down, f, res, 100));
        Assert.AreEqual("Box", res[0].collider.name);
        Assert.AreEqual(3.5f, res[0].point.y, 1e-3f, "ray hits the box surface (no skin)");
        Assert.AreEqual(Vector2.up, res[0].normal);
        Assert.AreEqual(0, res[1].point.y, 1e-3f, "then the ground");
        // start inside a collider
        var inside = Physics2D.Raycast(new Vector2(0, 3), Vector2.right, 10);
        Assert.AreEqual("Box", inside.collider.name);
        Assert.AreEqual(0, inside.fraction);
        Assert.AreEqual(Vector2.left, inside.normal, "normal opposes the ray when it starts inside");
        Physics2D.queriesStartInColliders = false;
        var outside = Physics2D.Raycast(new Vector2(0, 3), Vector2.right, 10);
        Assert.IsFalse(outside, "start-inside collider skipped when queriesStartInColliders is off");
        Physics2D.queriesStartInColliders = true;
        // layer mask
        box.gameObject.layer = 8;
        var masked = Physics2D.Raycast(new Vector2(0, 4.5f), Vector2.down, 10, ~(1 << 8));
        Assert.AreEqual("Ground", masked.collider.name);
    }

    [UnityTest] public IEnumerator ShapeCastsAndOverlaps()
    {
        Ground();
        var wall = new GameObject("Wall"); wall.transform.position = new Vector3(5, 2, 0);
        wall.AddComponent<BoxCollider2D>().size = new Vector2(1, 4);
        yield return null;
        var h = Physics2D.CircleCast(new Vector2(0, 2), 0.5f, Vector2.right, 20);
        Assert.AreEqual("Wall", h.collider.name);
        Assert.AreEqual(4.5f - 0.5f, h.centroid.x, 0.03f, "circle centre stops one radius before the wall face");
        Assert.AreEqual(-1, h.normal.x, 1e-3f);
        var bh = Physics2D.BoxCast(new Vector2(0, 2), new Vector2(1, 1), 0, Vector2.right, 20);
        Assert.AreEqual(4.0f, bh.centroid.x, 0.03f);
        Assert.IsNotNull(Physics2D.OverlapCircle(new Vector2(4.2f, 2), 0.5f));
        Assert.IsNull(Physics2D.OverlapCircle(new Vector2(3, 2), 0.5f));
        Assert.AreEqual(wall.GetComponent<Collider2D>(), Physics2D.OverlapPoint(new Vector2(5, 3)));
        var all = Physics2D.OverlapAreaAll(new Vector2(-1, -1), new Vector2(6, 3));
        Assert.AreEqual(2, all.Length);
        var cap = Physics2D.OverlapCapsule(new Vector2(5, 5), new Vector2(0.5f, 2), CapsuleDirection2D.Vertical, 0);
        Assert.IsNotNull(cap, "capsule reaching down into the wall top");
    }

    [UnityTest] public IEnumerator HingeJointPendulumKeepsLength()
    {
        var anchor = new GameObject("Anchor"); anchor.transform.position = new Vector3(0, 10, 0);
        var arb = anchor.AddComponent<Rigidbody2D>(); arb.bodyType = RigidbodyType2D.Static;
        var bob = Ball(new Vector2(3, 10), 0.25f);
        var hinge = bob.gameObject.AddComponent<HingeJoint2D>();
        hinge.connectedBody = arb; hinge.anchor = new Vector2(-3, 0);
        float maxErr = 0, minY = 10;
        for (int i = 0; i < 200; i++)
        {
            yield return new WaitForFixedUpdate();
            maxErr = Mathf.Max(maxErr, Mathf.Abs((bob.position - new Vector2(0, 10)).magnitude - 3));
            minY = Mathf.Min(minY, bob.position.y);
        }
        Assert.Less(maxErr, 0.02f, "rod length kept");
        Assert.AreEqual(7, minY, 0.05f, "swings through the bottom");
        Assert.AreNotEqual(0, hinge.jointAngle);
    }

    [UnityTest] public IEnumerator HingeMotorAndLimits()
    {
        var wheel = Ball(new Vector2(0, 5)); wheel.gravityScale = 0;
        var h = wheel.gameObject.AddComponent<HingeJoint2D>();
        h.useMotor = true; h.motor = new JointMotor2D { motorSpeed = 90, maxMotorTorque = 1000 };
        yield return Fixed(50);
        Assert.AreEqual(90, wheel.angularVelocity, 0.5f, "motor drives the speed (deg/s)");
        h.useMotor = false; h.useLimits = true; h.limits = new JointAngleLimits2D { min = -10, max = 10 };
        wheel.angularVelocity = 0;
        wheel.AddTorque(5, ForceMode2D.Impulse);
        yield return Fixed(60);
        Assert.That(h.jointAngle, Is.InRange(-12.5f, 12.5f), "limit holds (Box2D allows angularSlop = 2° past the limit)");
    }

    [UnityTest] public IEnumerator DistanceAndSpringJoints()
    {
        var a = new GameObject("A"); a.transform.position = new Vector3(0, 10, 0);
        var b = Ball(new Vector2(0, 7), 0.2f);
        var dj = b.gameObject.AddComponent<DistanceJoint2D>();
        dj.connectedBody = null; dj.connectedAnchor = new Vector2(0, 10); dj.autoConfigureConnectedAnchor = false;
        yield return Fixed(50);
        Assert.AreEqual(3, dj.distance, 1e-3f, "auto-configured distance");
        Assert.AreEqual(7, b.position.y, 0.02f, "hangs at the joint distance");

        var c = Ball(new Vector2(5, 8), 0.2f);
        var sj = c.gameObject.AddComponent<SpringJoint2D>();
        sj.autoConfigureConnectedAnchor = false; sj.connectedAnchor = new Vector2(5, 10);
        sj.autoConfigureDistance = false; sj.distance = 2; sj.frequency = 2; sj.dampingRatio = 1;
        yield return Fixed(200);
        // spring k = m ω², equilibrium stretch = g / ω²
        float w = 2 * Mathf.PI * 2, stretch = 9.81f / (w * w);
        Assert.AreEqual(8 - stretch, c.position.y, 0.02f, "spring equilibrium");
    }

    [UnityTest] public IEnumerator MovePositionDrivesKinematicBodyAndPushesDynamic()
    {
        var platform = Box(new Vector2(0, 0), new Vector2(4, 0.5f)); platform.bodyType = RigidbodyType2D.Kinematic;
        var rider = Box(new Vector2(0, 0.76f), Vector2.one);
        yield return Fixed(20);
        for (int i = 0; i < 50; i++)
        {
            platform.MovePosition(platform.position + new Vector2(0, 2 * Time.fixedDeltaTime));
            yield return new WaitForFixedUpdate();
        }
        Assert.AreEqual(2, platform.position.y, 1e-3f, "kinematic MovePosition reaches its targets");
        Assert.AreEqual(2.76f, rider.position.y, 0.05f, "dynamic body carried up");
        Assert.AreEqual(0, platform.velocity.y, 1e-5f, "velocity back to zero after the move step");
    }

    [UnityTest] public IEnumerator ConstraintsAndImpulses()
    {
        var rb = Ball(new Vector2(0, 50)); rb.constraints = RigidbodyConstraints2D.FreezePositionY;
        rb.AddForce(new Vector2(3, 3), ForceMode2D.Impulse);
        yield return Fixed(5);
        Assert.AreEqual(3, rb.velocity.x, 1e-4f, "impulse / mass");
        Assert.AreEqual(0, rb.velocity.y, 1e-6f, "frozen axis");
        Assert.AreEqual(50, rb.position.y, 1e-6f);
        rb.freezeRotation = true; rb.AddTorque(10, ForceMode2D.Impulse);
        yield return Fixed(2);
        Assert.AreEqual(0, rb.angularVelocity, 1e-6f, "frozen rotation");
    }

    [UnityTest] public IEnumerator OneWayPlatformLetsBodiesThroughFromBelow()
    {
        var plat = new GameObject("Platform"); plat.transform.position = new Vector3(0, 3, 0);
        var pc = plat.AddComponent<BoxCollider2D>(); pc.size = new Vector2(4, 0.2f); pc.usedByEffector = true;
        plat.AddComponent<PlatformEffector2D>();
        var rb = Ball(new Vector2(0, 0.5f), 0.3f); rb.velocity = new Vector2(0, 10);
        yield return Fixed(150);
        Assert.AreEqual(3.1f + 0.3f, rb.position.y, 0.05f, "jumped through from below, landed on top");
    }

    [UnityTest] public IEnumerator EdgeAndPolygonColliders()
    {
        var ground = new GameObject("Edge");
        var e = ground.AddComponent<EdgeCollider2D>();
        e.points = new[] { new Vector2(-10, 0), new Vector2(0, 0), new Vector2(10, 2) };
        var g = new GameObject("L"); g.transform.position = new Vector3(-5, 3, 0);
        var poly = g.AddComponent<PolygonCollider2D>();
        poly.points = new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(0, 2) };   // concave L
        var rb = g.AddComponent<Rigidbody2D>();
        yield return Fixed(5);
        Assert.Greater(poly.shapeCount, 1, "concave polygon split into convex pieces");
        yield return Fixed(150);
        Assert.AreEqual(0, rb.position.y, 0.05f, "L shape rests on the edge chain");
        Assert.AreEqual(0, rb.rotation, 1f);
        Assert.IsTrue(poly.IsTouching(e));
    }
}
