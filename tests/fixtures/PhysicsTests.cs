using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PhysicsTests
{
    static GameObject Ground(float y = 0, float angleZ = 0, PhysicMaterial mat = null)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "Ground";
        g.transform.localScale = new Vector3(40, 1, 40);
        g.transform.position = new Vector3(0, y - 0.5f, 0);
        g.transform.rotation = Quaternion.Euler(0, 0, angleZ);
        if (mat != null) g.GetComponent<Collider>().sharedMaterial = mat;
        return g;
    }

    static Rigidbody Ball(Vector3 p, float r = 0.5f)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.transform.position = p; g.transform.localScale = Vector3.one * (2 * r);
        return g.AddComponent<Rigidbody>();
    }

    static Rigidbody Box(Vector3 p, Vector3 size, float mass = 1)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.transform.position = p; g.transform.localScale = size;
        var rb = g.AddComponent<Rigidbody>(); rb.mass = mass; return rb;
    }

    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    [UnityTest] public IEnumerator FreeFallMatchesSemiImplicitEuler()
    {
        var rb = Ball(new Vector3(0, 100, 0));
        yield return Fixed(25);
        float dt = Time.fixedDeltaTime, g = Physics.gravity.y;
        int n = 25;
        float expectY = 100 + g * dt * dt * n * (n + 1) / 2;   // v += g dt; y += v dt
        Assert.AreEqual(expectY, rb.position.y, 1e-3f, "position");
        Assert.AreEqual(g * dt * n, rb.velocity.y, 1e-4f, "velocity");
    }

    [UnityTest] public IEnumerator DragDampsLikePhysX()
    {
        var rb = Ball(new Vector3(0, 100, 0)); rb.useGravity = false; rb.drag = 2;
        rb.velocity = new Vector3(10, 0, 0);
        yield return Fixed(10);
        float expect = 10 * Mathf.Pow(1 - 2 * Time.fixedDeltaTime, 10);   // v *= 1 - drag·dt
        Assert.AreEqual(expect, rb.velocity.x, 1e-3f);
    }

    [UnityTest] public IEnumerator BallComesToRestOnGroundAndSleeps()
    {
        Ground();
        var rb = Ball(new Vector3(0, 3, 0));
        yield return Fixed(200);
        Assert.AreEqual(0.5f, rb.position.y, 0.02f);
        Assert.IsTrue(rb.IsSleeping(), "sleeping after settling");
    }

    class TriggerCounter : MonoBehaviour
    {
        public int Enters, Exits;
        void OnTriggerEnter(Collider other) => Enters++;
        void OnTriggerExit(Collider other) => Exits++;
    }

    [UnityTest] public IEnumerator TogglingIsKinematicRefiresTriggers()
    {
        var zone = GameObject.CreatePrimitive(PrimitiveType.Cube); zone.transform.position = new Vector3(40, 0, 0);
        zone.GetComponent<Collider>().isTrigger = true;
        var counter = zone.AddComponent<TriggerCounter>();
        var rb = Ball(new Vector3(40, 0, 0)); rb.useGravity = false;
        yield return Fixed(3);
        Assert.AreEqual(1, counter.Enters);
        rb.isKinematic = true;   // PhysX re-filters the actor's pairs: lost, then found again while still overlapping
        yield return Fixed(3);
        Assert.AreEqual(1, counter.Exits);
        Assert.AreEqual(2, counter.Enters);
    }

    [UnityTest] public IEnumerator SleepingBallWakesWhenItsSupportIsTeleportedAway()
    {
        var support = Box(new Vector3(0, 0.5f, 0), Vector3.one);
        support.isKinematic = true;
        var rb = Ball(new Vector3(0, 1.5f, 0));
        yield return Fixed(200);
        Assert.IsTrue(rb.IsSleeping(), "settled on the kinematic box");
        support.transform.position = new Vector3(20, 0.5f, 0);   // teleported, no MovePosition: PhysX lost touch wakes the ball
        yield return Fixed(30);
        Assert.IsFalse(rb.IsSleeping());
        Assert.Less(rb.position.y, 0.5f, "falls once nothing holds it");
    }

    [UnityTest] public IEnumerator BouncyBallReboundsAboveThreshold()
    {
        var mat = new PhysicMaterial { bounciness = 1, bounceCombine = PhysicMaterialCombine.Maximum };
        Ground();
        var rb = Ball(new Vector3(0, 5, 0)); rb.GetComponent<Collider>().sharedMaterial = mat;
        float maxUp = 0; bool landed = false; float apex = 0;
        for (int i = 0; i < 150; i++)
        {
            yield return new WaitForFixedUpdate();
            if (rb.velocity.y > maxUp) maxUp = rb.velocity.y;
            if (rb.position.y < 0.6f) landed = true;
            if (landed) apex = Mathf.Max(apex, rb.position.y);
        }
        Assert.Greater(maxUp, 8.5f, "rebound speed ~ impact speed (sqrt(2·9.81·4.5) = 9.4)");
        Assert.Greater(apex, 4.0f, "bounces back close to the drop height");
    }

    [UnityTest] public IEnumerator StackOfBoxesStaysUp()
    {
        Ground();
        var boxes = new List<Rigidbody>();
        for (int i = 0; i < 4; i++) boxes.Add(Box(new Vector3(0, 0.5f + i * 1.0f, 0), Vector3.one));
        yield return Fixed(300);
        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual(0.5f + i, boxes[i].position.y, 0.05f, $"box {i} height");
            Assert.AreEqual(0, boxes[i].position.x, 0.05f, $"box {i} x");
            Assert.Less(Quaternion.Angle(Quaternion.identity, boxes[i].rotation), 2f, $"box {i} upright");
        }
    }

    [UnityTest] public IEnumerator StaticFrictionHoldsOnInclineBelowAngle()
    {
        var mat = new PhysicMaterial { staticFriction = 0.7f, dynamicFriction = 0.7f };
        Ground(0, 25, mat);            // tan 25° = 0.47 < 0.7 → holds
        var up = Quaternion.Euler(0, 0, 25) * Vector3.up;
        var rb = Box(up * 0.52f, Vector3.one); rb.transform.rotation = Quaternion.Euler(0, 0, 25);
        rb.GetComponent<Collider>().sharedMaterial = mat;
        yield return Fixed(10);
        var p0 = rb.position;
        yield return Fixed(100);
        Assert.Less((rb.position - p0).magnitude, 0.05f, "box does not slide");
    }

    static IEnumerator Slide(float[] result)
    {
        var mat = new PhysicMaterial { staticFriction = 0.2f, dynamicFriction = 0.2f, frictionCombine = PhysicMaterialCombine.Minimum };
        Ground(0, 30, mat);
        var rot = Quaternion.Euler(0, 0, 30);
        var rb = Box(rot * Vector3.up * 0.5f, Vector3.one); rb.transform.rotation = rot;
        rb.GetComponent<Collider>().sharedMaterial = mat;
        yield return Fixed(10);
        var v0 = rb.velocity.magnitude;
        yield return Fixed(25);
        result[0] = (rb.velocity.magnitude - v0) / (25 * Time.fixedDeltaTime);
    }

    // PhysX patch friction: two anchors per patch, each limited by μ·N of the whole patch → boxes get up to twice the friction
    [UnityTest] public IEnumerator BoxSlidesWithPatchFriction()
    {
        var a = new float[1]; Physics.improvedPatchFriction = false;
        yield return Slide(a);
        float expect = 9.81f * (Mathf.Sin(30 * Mathf.Deg2Rad) - 2 * 0.2f * Mathf.Cos(30 * Mathf.Deg2Rad));   // 1.51 m/s²
        Assert.AreEqual(expect, a[0], 0.2f);
    }

    [UnityTest] public IEnumerator BoxSlidesWithImprovedPatchFriction()
    {
        var a = new float[1]; Physics.improvedPatchFriction = true;
        yield return Slide(a);
        Physics.improvedPatchFriction = false;
        float expect = 9.81f * (Mathf.Sin(30 * Mathf.Deg2Rad) - 0.2f * Mathf.Cos(30 * Mathf.Deg2Rad));   // 3.21 m/s² (analytical)
        Assert.AreEqual(expect, a[0], 0.2f);
    }

    [UnityTest] public IEnumerator QueriesHitTheRightColliders()
    {
        var ground = Ground();
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.transform.position = new Vector3(3, 0.5f, 0); box.name = "Crate";
        var trig = GameObject.CreatePrimitive(PrimitiveType.Sphere); trig.transform.position = new Vector3(-3, 1, 0); trig.GetComponent<Collider>().isTrigger = true;
        yield return null;
        Assert.IsTrue(Physics.Raycast(new Vector3(3, 5, 0), Vector3.down, out var hit, 10));
        Assert.AreEqual("Crate", hit.collider.name); Assert.AreEqual(1f, hit.point.y, 1e-3f); Assert.AreEqual(4f, hit.distance, 1e-3f);
        Assert.AreEqual(Vector3.up, hit.normal);
        Assert.IsTrue(Physics.Raycast(new Vector3(0, 5, 0), Vector3.down, out hit)); Assert.AreEqual("Ground", hit.collider.name);
        Assert.IsFalse(Physics.Raycast(new Vector3(3, 0.5f, 0), Vector3.up, 10), "ray starting inside a collider does not hit it");
        Assert.AreEqual(2, Physics.RaycastAll(new Vector3(3, 5, 0), Vector3.down, 10).Length);
        Assert.IsTrue(Physics.Raycast(new Vector3(-3, 5, 0), Vector3.down, out hit, 10)); Assert.AreEqual("Sphere", hit.collider.name, "queries hit triggers by default");
        Assert.IsTrue(Physics.Raycast(new Vector3(-3, 5, 0), Vector3.down, out hit, 10, ~0, QueryTriggerInteraction.Ignore)); Assert.AreEqual("Ground", hit.collider.name);
        Assert.IsTrue(Physics.SphereCast(new Vector3(3, 5, 0), 0.25f, Vector3.down, out hit, 10));
        Assert.AreEqual(3.75f, hit.distance, 0.01f, "sphere cast distance = 5 − 1 − 0.25");
        Assert.IsTrue(Physics.BoxCast(new Vector3(-0.0f, 0.5f, 0), Vector3.one * 0.25f, Vector3.right, out hit, Quaternion.identity, 10));
        Assert.AreEqual("Crate", hit.collider.name); Assert.AreEqual(2.25f, hit.distance, 0.01f);
        var cols = Physics.OverlapSphere(new Vector3(3, 1.2f, 0), 0.5f);
        CollectionAssert.AreEquivalent(new[] { "Crate" }, System.Array.ConvertAll(cols, c => c.name));
        Assert.IsTrue(Physics.CheckBox(new Vector3(3, 0.5f, 0), Vector3.one * 0.1f));
        Assert.AreEqual(new Vector3(3.5f, 0.5f, 0), box.GetComponent<Collider>().ClosestPoint(new Vector3(10, 0.5f, 0)));
        Physics.IgnoreLayerCollision(0, 0, false);
    }

    class Recorder : MonoBehaviour
    {
        public List<string> events = new();
        public float impact;
        void OnCollisionEnter(Collision c) { events.Add("enter:" + c.collider.name); impact = c.relativeVelocity.magnitude; }
        void OnCollisionExit(Collision c) => events.Add("exit:" + c.collider.name);
        void OnTriggerEnter(Collider c) => events.Add("tenter:" + c.name);
        void OnTriggerExit(Collider c) => events.Add("texit:" + c.name);
    }

    [UnityTest] public IEnumerator CallbacksEnterExitAndTriggers()
    {
        Ground();
        var zone = GameObject.CreatePrimitive(PrimitiveType.Cube); zone.name = "Zone"; zone.transform.position = new Vector3(0, 3, 0);
        zone.transform.localScale = new Vector3(2, 1, 2); zone.GetComponent<Collider>().isTrigger = true;
        var rb = Ball(new Vector3(0, 6, 0));
        var rec = rb.gameObject.AddComponent<Recorder>();
        yield return Fixed(100);
        CollectionAssert.AreEqual(new[] { "tenter:Zone", "texit:Zone", "enter:Ground" }, rec.events.GetRange(0, 3));
        Assert.Greater(rec.impact, 9f, "relative velocity at impact ≈ sqrt(2·9.81·5.5) = 10.4");
        rb.AddForce(Vector3.up * 8, ForceMode.VelocityChange);
        yield return Fixed(10);
        Assert.Contains("exit:Ground", rec.events);
    }

    [UnityTest] public IEnumerator CharacterControllerWalksStepsAndStopsAtWalls()
    {
        Ground();
        var step = GameObject.CreatePrimitive(PrimitiveType.Cube); step.transform.position = new Vector3(3, 0.1f, 0); step.transform.localScale = new Vector3(2, 0.2f, 4);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(8, 1.5f, 0); wall.transform.localScale = new Vector3(1, 3, 4);
        var go = new GameObject("Player"); go.transform.position = new Vector3(0, 1.0f + 0.08f, 0);
        var cc = go.AddComponent<CharacterController>();
        for (int i = 0; i < 300; i++) { cc.SimpleMove(new Vector3(3, 0, 0)); yield return null; }
        Assert.IsTrue(cc.isGrounded, "grounded");
        Assert.AreEqual(8 - 0.5f - 0.5f - cc.skinWidth, go.transform.position.x, 0.1f, "stopped by the wall (radius + skin)");
        go.transform.position = new Vector3(3, 2, 0);
        for (int i = 0; i < 60; i++) { cc.SimpleMove(Vector3.zero); yield return null; }
        Assert.AreEqual(0.2f + 1 + cc.skinWidth, go.transform.position.y, 0.05f, "stands on the step");
    }

    [UnityTest] public IEnumerator KinematicPlatformPushesDynamicBox()
    {
        Ground();
        var pusher = Box(new Vector3(-2, 0.5f, 0), Vector3.one); pusher.isKinematic = true;
        var crate = Box(new Vector3(0, 0.5f, 0), Vector3.one);
        for (int i = 0; i < 60; i++) { pusher.MovePosition(pusher.position + Vector3.right * 2 * Time.fixedDeltaTime); yield return new WaitForFixedUpdate(); }
        Assert.Greater(crate.position.x, 0.8f, "crate pushed");
        Assert.Greater(crate.position.x - pusher.position.x, 0.95f, "no interpenetration");
    }

    [UnityTest] public IEnumerator RestsOnPlaneMeshCollider()
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.transform.localScale = Vector3.one * 4;
        Assert.IsInstanceOf<MeshCollider>(plane.GetComponent<Collider>());
        var ball = Ball(new Vector3(0.3f, 2, 0.7f));
        var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule); cap.transform.position = new Vector3(3, 2, 0); cap.transform.rotation = Quaternion.Euler(0, 0, 90);
        var crb = cap.AddComponent<Rigidbody>();
        var box = Box(new Vector3(-3, 2, 0), Vector3.one);
        yield return Fixed(200);
        Assert.AreEqual(0.5f, ball.position.y, 0.03f, "ball on plane mesh");
        Assert.AreEqual(0.5f, crb.position.y, 0.03f, "capsule lying on its side (radius 0.5)");
        Assert.AreEqual(0.5f, box.position.y, 0.03f, "box on plane mesh");
    }

    [UnityTest] public IEnumerator ConvexMeshColliderFallsAndRests()
    {
        Ground();
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(g.GetComponent<Collider>());
        var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh; mc.convex = true;
        g.transform.position = new Vector3(0, 3, 0);
        var rb = g.AddComponent<Rigidbody>();
        yield return Fixed(200);
        Assert.AreEqual(1f, rb.position.y, 0.05f, "cylinder (height 2) standing on the ground");
    }

    [UnityTest] public IEnumerator TorqueGivesAngularAccelerationFromInertia()
    {
        var rb = Box(new Vector3(0, 50, 0), new Vector3(2, 1, 1), 3); rb.useGravity = false; rb.angularDrag = 0;
        yield return Fixed(1);
        // box 2×1×1, mass 3: I_y = m/12 (x² + z²) = 3/12 (4 + 1) = 1.25
        Assert.AreEqual(1.25f, rb.inertiaTensor.y, 1e-3f);
        rb.AddTorque(Vector3.up * 2.5f);
        yield return Fixed(1);
        Assert.AreEqual(2.5f / 1.25f * Time.fixedDeltaTime, rb.angularVelocity.y, 1e-4f, "ω = τ/I·dt");
    }

    [UnityTest] public IEnumerator FastThinWallTunnelsInDiscreteMode()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(5, 50, 0); wall.transform.localScale = new Vector3(0.1f, 5, 5);
        var rb = Ball(new Vector3(0, 50, 0), 0.1f); rb.useGravity = false;
        rb.velocity = new Vector3(400, 0, 0);
        yield return Fixed(3);
        Assert.Greater(rb.position.x, 5.5f, "discrete collision detection misses a thin wall at 8 m per step (as in Unity)");
    }

    [UnityTest] public IEnumerator PileOfBoxesSettles()
    {
        Ground();
        var list = new List<Rigidbody>();
        for (int y = 0; y < 4; y++) for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++)
            list.Add(Box(new Vector3(x * 1.02f - 1, 0.5f + y * 1.02f, z * 1.02f - 1), Vector3.one));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        yield return Fixed(250);
        foreach (var rb in list) Assert.Greater(rb.position.y, 0.4f, "nothing falls through the ground");
        int resting = 0; foreach (var rb in list) if (rb.IsSleeping() || rb.velocity.magnitude < 0.05f) resting++;
        Assert.AreEqual(list.Count, resting, "pile comes to rest");
        Debug.LogWarning($"36 boxes, 250 steps: {sw.ElapsedMilliseconds} ms");
    }

    [UnityTest] public IEnumerator TallTowerNeedsMoreSolverIterations()
    {
        Ground();
        var list = new List<Rigidbody>();
        for (int i = 0; i < 12; i++) { var rb = Box(new Vector3(0, 0.5f + i, 0), Vector3.one); rb.solverIterations = 20; list.Add(rb); }
        yield return Fixed(300);
        Assert.AreEqual(11.5f, list[11].position.y, 0.05f, "12-box tower stands with 20 iterations");
        Assert.AreEqual(0, list[11].position.x, 0.2f);
    }
}
