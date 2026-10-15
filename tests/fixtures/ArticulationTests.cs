using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ArticulationTests
{
    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    // root at the origin, one link 1 m along +X hanging from it on a joint at the root
    static (ArticulationBody root, ArticulationBody link) Arm(ArticulationJointType type, bool immovableRoot = true)
    {
        var root = new GameObject("Root");
        var r = root.AddComponent<ArticulationBody>();
        r.immovable = immovableRoot;
        var linkGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        linkGo.name = "Link";
        linkGo.transform.SetParent(root.transform, false);
        linkGo.transform.localPosition = new Vector3(1, 0, 0);
        linkGo.transform.localScale = Vector3.one * 0.2f;
        var l = linkGo.AddComponent<ArticulationBody>();
        l.jointType = type;
        l.anchorPosition = new Vector3(-1 / 0.2f, 0, 0);   // anchor at the root (link space is scaled)
        l.mass = 1;
        return (r, l);
    }

    [UnityTest] public IEnumerator RevoluteLinkSwingsDownAndKeepsItsLength()
    {
        var (_, link) = Arm(ArticulationJointType.RevoluteJoint);
        link.anchorRotation = Quaternion.Euler(0, 90, 0);   // joint X axis = world Z: swings in the XY plane
        link.twistLock = ArticulationDofLock.FreeMotion;
        yield return Fixed(25);
        Vector3 p = link.transform.position;
        Assert.Less(p.y, -0.2f, "gravity swings the link down");
        Assert.AreEqual(1f, p.magnitude, 0.02f, "the joint keeps the link 1 m from the anchor");
        Assert.AreEqual(0f, p.z, 0.01f, "it stays in the joint plane");
        Assert.AreEqual(1, link.dofCount);
        Assert.Greater(Mathf.Abs(link.jointPosition[0]), 0.2f);
    }

    [UnityTest] public IEnumerator RevoluteDriveReachesItsTarget()
    {
        var (_, link) = Arm(ArticulationJointType.RevoluteJoint);
        link.useGravity = false;
        link.anchorRotation = Quaternion.Euler(0, 90, 0);
        link.twistLock = ArticulationDofLock.FreeMotion;
        var d = link.xDrive; d.stiffness = 1000; d.damping = 100; d.forceLimit = float.MaxValue; d.target = 45; link.xDrive = d;
        yield return Fixed(150);
        Assert.AreEqual(45f * Mathf.Deg2Rad, Mathf.Abs(link.jointPosition[0]), 0.03f, "the drive holds the target angle");
        Assert.Less(Mathf.Abs(link.jointVelocity[0]), 0.05f);
    }

    [UnityTest] public IEnumerator StiffDriveStaysStable()
    {
        var (_, link) = Arm(ArticulationJointType.RevoluteJoint);
        link.anchorRotation = Quaternion.Euler(0, 90, 0);
        link.twistLock = ArticulationDofLock.FreeMotion;
        var d = link.xDrive; d.stiffness = 1e6f; d.damping = 1e4f; d.forceLimit = float.MaxValue; d.target = 0; link.xDrive = d;
        yield return Fixed(100);
        Assert.Less(link.transform.position.y, 0f);
        Assert.Greater(link.transform.position.y, -0.05f, "a very stiff drive holds the arm level against gravity without blowing up");
    }

    [UnityTest] public IEnumerator PrismaticLinkSlidesToItsLimit()
    {
        var (_, link) = Arm(ArticulationJointType.PrismaticJoint);
        link.anchorRotation = Quaternion.Euler(0, 0, 90);   // joint X axis = world Y
        link.linearLockX = ArticulationDofLock.LimitedMotion;
        var d = link.xDrive; d.lowerLimit = -0.5f; d.upperLimit = 0.5f; link.xDrive = d;
        yield return Fixed(60);
        Assert.AreEqual(-0.5f, link.jointPosition[0], 0.03f, "falls to the lower limit along the axis");
        Assert.AreEqual(-0.5f, link.transform.position.y, 0.03f);
        Assert.AreEqual(1f, link.transform.position.x, 0.01f, "no motion off the axis");
    }

    [UnityTest] public IEnumerator FixedLinkFollowsAFallingRoot()
    {
        var (root, link) = Arm(ArticulationJointType.FixedJoint, immovableRoot: false);
        root.gameObject.AddComponent<SphereCollider>().radius = 0.1f;
        yield return Fixed(20);
        Assert.Less(root.transform.position.y, -0.1f, "the free root falls");
        Assert.AreEqual(1f, Vector3.Distance(root.transform.position, link.transform.position), 0.02f);
        Assert.AreEqual(0, link.dofCount);
        Assert.IsTrue(root.isRoot);
        Assert.IsFalse(link.isRoot);
        Assert.AreEqual(0, root.index);
        Assert.AreEqual(1, link.index);
    }

    [UnityTest] public IEnumerator JointPositionsRoundTrip()
    {
        var (root, link) = Arm(ArticulationJointType.RevoluteJoint);
        link.useGravity = false;
        link.anchorRotation = Quaternion.Euler(0, 90, 0);
        link.twistLock = ArticulationDofLock.FreeMotion;
        yield return Fixed(1);
        root.SetJointPositions(new List<float> { 0.5f });
        yield return Fixed(1);
        var got = new List<float>();
        Assert.AreEqual(1, root.GetJointPositions(got));
        Assert.AreEqual(0.5f, Mathf.Abs(got[0]), 0.02f);
        Assert.AreEqual(1f, link.transform.position.magnitude, 0.02f);
        Assert.AreEqual(Mathf.Cos(0.5f), link.transform.position.x, 0.03f);
    }

    [UnityTest] public IEnumerator CollidersOnALinkCollide()
    {
        var (_, link) = Arm(ArticulationJointType.RevoluteJoint);
        link.anchorRotation = Quaternion.Euler(0, 90, 0);
        link.twistLock = ArticulationDofLock.FreeMotion;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(1, -0.65f, 0); floor.transform.localScale = new Vector3(1, 1, 1);
        yield return Fixed(60);
        Assert.Greater(link.transform.position.y, -0.2f, "the link rests on the box instead of swinging through it");
        Assert.AreEqual(link, link.GetComponent<Collider>().attachedArticulationBody);
        Assert.IsNull(link.GetComponent<Collider>().attachedRigidbody, "articulation colliders have no attached Rigidbody");
    }

    [UnityTest] public IEnumerator LongLightChainUnderAHeavyTipStaysRigid()
    {
        var root = new GameObject("Root"); root.AddComponent<ArticulationBody>().immovable = true;
        Transform parent = root.transform;
        var links = new List<ArticulationBody>();
        for (int i = 0; i < 6; i++)
        {
            var g = new GameObject("L" + i);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = new Vector3(0.5f, 0, 0);
            var ab = g.AddComponent<ArticulationBody>();
            ab.jointType = ArticulationJointType.SphericalJoint;
            ab.twistLock = ab.swingYLock = ab.swingZLock = ArticulationDofLock.FreeMotion;
            ab.anchorPosition = new Vector3(-0.5f, 0, 0);
            ab.mass = i == 5 ? 100 : 0.05f;
            links.Add(ab); parent = g.transform;
        }
        yield return Fixed(80);
        Vector3 prev = Vector3.zero;
        foreach (var ab in links)
        {
            Assert.AreEqual(0.5f, Vector3.Distance(prev, ab.transform.position), 0.01f, ab.name + " stays 0.5 m from its parent");
            prev = ab.transform.position;
        }
        Assert.Less(links[5].transform.position.y, -1f, "the chain hangs down");
        Assert.AreEqual(3, links[0].dofCount);
    }
}
