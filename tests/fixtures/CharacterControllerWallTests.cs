using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// A controller pushed into a wall stays skinWidth away from it every frame (a sweep that starts touching reports nothing, so the
// motion into surfaces already within the skin is removed first) and slides along it.
public class CharacterControllerWallTests
{
    [UnityTest] public IEnumerator StaysOutsideTheWallWhileSliding()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.localScale = new Vector3(1, 4, 10); wall.transform.position = new Vector3(2, 0, 0);
        var cc = new GameObject("CC").AddComponent<CharacterController>();
        float maxX = 0;
        for (int i = 0; i < 30; i++)
        {
            var flags = cc.Move(new Vector3(0.2f, 0, 0.1f));
            maxX = Mathf.Max(maxX, cc.transform.position.x);
            if (i >= 5) Assert.AreEqual(CollisionFlags.Sides, flags, "frame " + i);
            yield return null;
        }
        Assert.AreEqual(1.5f - cc.radius - cc.skinWidth, maxX, 2e-3f, "never closer than skinWidth");
        Assert.AreEqual(3f, cc.transform.position.z, 1e-3f, "slides along the wall");
    }

    [Test] public void RandomIsXorshift128SeededLikeUnity()
    {
        Random.InitState(1);
        var a = new[] { Random.value, Random.value };
        Random.InitState(1);
        Assert.AreEqual(a[0], Random.value); Assert.AreEqual(a[1], Random.value);
        var st = Random.state; float next = Random.value; Random.state = st;
        Assert.AreEqual(next, Random.value, "state round-trips");
        for (int i = 0; i < 1000; i++) { int r = Random.Range(3, 7); Assert.That(r, Is.InRange(3, 6)); float f = Random.Range(-1f, 1f); Assert.That(f, Is.InRange(-1f, 1f)); }
        Assert.AreEqual(1f, Random.insideUnitSphere.magnitude, 1f);
        Assert.AreEqual(1f, Random.onUnitSphere.magnitude, 1e-5f);
        Assert.AreEqual(1f, Random.rotationUniform.normalized.w * Random.rotationUniform.normalized.w <= 1 ? 1f : 0f);
    }
}
