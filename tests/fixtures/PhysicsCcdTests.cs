using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PhysicsCcdTests
{
    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    static Rigidbody Shoot(CollisionDetectionMode mode, float speed)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Wall";
        wall.transform.position = new Vector3(5, 0, 0); wall.transform.localScale = new Vector3(0.1f, 4, 4);
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); ball.name = "Ball";
        ball.transform.localScale = Vector3.one * 0.2f;
        var rb = ball.AddComponent<Rigidbody>();
        rb.useGravity = false; rb.collisionDetectionMode = mode; rb.velocity = new Vector3(speed, 0, 0);
        return rb;
    }

    [UnityTest] public IEnumerator DiscreteTunnels()
    {
        var rb = Shoot(CollisionDetectionMode.Discrete, 200);
        yield return Fixed(5);
        Assert.Greater(rb.position.x, 6);
    }

    [UnityTest] public IEnumerator ContinuousStopsAtWall([Values(CollisionDetectionMode.Continuous, CollisionDetectionMode.ContinuousDynamic, CollisionDetectionMode.ContinuousSpeculative)] CollisionDetectionMode mode)
    {
        var rb = Shoot(mode, 200);
        yield return Fixed(5);
        Assert.Less(rb.position.x, 5, mode + " keeps the ball in front of the wall");
        Assert.Greater(rb.position.x, 4.5f);
        Assert.Less(rb.velocity.x, 1f, "the approaching velocity (200) is removed");
    }
}
