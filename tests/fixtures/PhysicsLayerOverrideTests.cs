using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Collider / Rigidbody includeLayers, excludeLayers and layerOverridePriority (Unity 2022.2+) in the 3D contact filter.
public class PhysicsLayerOverrideTests
{
    const int A = 8, B = 9;

    static GameObject Ground(int layer)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.transform.localScale = new Vector3(20, 1, 20); g.transform.position = new Vector3(0, -0.5f, 0);
        g.layer = layer;
        return g;
    }

    static Rigidbody Ball(int layer)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.transform.position = new Vector3(0, 2, 0); g.layer = layer;
        return g.AddComponent<Rigidbody>();
    }

    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    [SetUp] public void Matrix() { for (int i = 0; i < 32; i++) { Physics.IgnoreLayerCollision(A, i, false); Physics.IgnoreLayerCollision(B, i, false); } }

    [UnityTest] public IEnumerator ColliderExcludeLayersLetsTheBallFallThrough()
    {
        var g = Ground(A); g.GetComponent<Collider>().excludeLayers = 1 << B;
        var rb = Ball(B);
        yield return Fixed(60);
        Assert.Less(rb.position.y, -1f);
    }

    [UnityTest] public IEnumerator RigidbodyExcludeLayersAppliesToItsColliders()
    {
        Ground(A);
        var rb = Ball(B); rb.excludeLayers = 1 << A;
        yield return Fixed(60);
        Assert.Less(rb.position.y, -1f);
    }

    [UnityTest] public IEnumerator IncludeLayersOverridesTheCollisionMatrix()
    {
        Physics.IgnoreLayerCollision(A, B, true);
        Ground(A);
        var rb = Ball(B); rb.GetComponent<Collider>().includeLayers = 1 << A;
        yield return Fixed(80);
        Assert.AreEqual(0.5f, rb.position.y, 0.05f);
    }

    [UnityTest] public IEnumerator HigherPriorityDecides()
    {
        var g = Ground(A); var gc = g.GetComponent<Collider>(); gc.excludeLayers = 1 << B; gc.layerOverridePriority = 0;
        var rb = Ball(B); var bc = rb.GetComponent<Collider>(); bc.includeLayers = 1 << A; bc.layerOverridePriority = 5;
        yield return Fixed(80);
        Assert.AreEqual(0.5f, rb.position.y, 0.05f, "the ball's include wins");
        bc.layerOverridePriority = -1;
        rb.position = new Vector3(0, 2, 0); rb.velocity = Vector3.zero; rb.WakeUp();
        yield return Fixed(60);
        Assert.Less(rb.position.y, -1f, "now the ground's exclude wins");
    }

    [Test] public void SerializedValuesRoundTrip()
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Collider>();
        c.includeLayers = 1 << 3; c.excludeLayers = 1 << 4; c.layerOverridePriority = 2;
        Assert.AreEqual(1 << 3, c.includeLayers.value); Assert.AreEqual(1 << 4, c.excludeLayers.value); Assert.AreEqual(2, c.layerOverridePriority);
        var rb = c.gameObject.AddComponent<Rigidbody>(); rb.excludeLayers = 1 << 6;
        Assert.AreEqual(1 << 6, rb.excludeLayers.value);
    }
}
