using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class JointProjectionTests
{
    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    // five light links under a heavy weight with a single solver iteration: the chain stretches unless projected
    static Rigidbody[] Chain(bool project)
    {
        var links = new Rigidbody[6];
        Rigidbody prev = null;
        for (int i = 0; i < 6; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = "Link" + i;
            g.transform.position = new Vector3(0, 10 - i, 0); g.transform.localScale = Vector3.one * 0.3f;
            Object.Destroy(g.GetComponent<Collider>());
            var rb = g.AddComponent<Rigidbody>(); rb.mass = i == 5 ? 100 : 0.05f; rb.solverIterations = 1; rb.solverVelocityIterations = 1;
            var j = g.AddComponent<ConfigurableJoint>();
            j.connectedBody = prev; j.anchor = new Vector3(0, 1 / 0.3f, 0);
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            if (project) { j.projectionMode = JointProjectionMode.PositionAndRotation; j.projectionDistance = 0.02f; j.projectionAngle = 180; }
            if (i == 0) { j.autoConfigureConnectedAnchor = false; j.connectedAnchor = new Vector3(0, 11, 0); }
            links[i] = rb; prev = rb;
        }
        return links;
    }

    static float Stretch(Rigidbody[] links)
    {
        float worst = Mathf.Abs(Vector3.Distance(links[0].position, new Vector3(0, 11, 0)) - 1);
        for (int i = 1; i < links.Length; i++) worst = Mathf.Max(worst, Mathf.Abs(Vector3.Distance(links[i].position, links[i - 1].position) - 1));
        return worst;
    }

    [UnityTest] public IEnumerator WithoutProjectionTheChainStretches()
    {
        var links = Chain(false);
        yield return Fixed(30);
        Assert.Greater(Stretch(links), 0.05f);
    }

    [UnityTest] public IEnumerator ProjectionKeepsLinksWithinTolerance()
    {
        var links = Chain(true);
        yield return Fixed(30);
        Assert.LessOrEqual(Stretch(links), 0.021f, "every link error clamped by projectionDistance");
    }
}
