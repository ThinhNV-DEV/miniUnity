using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class Physics2DCcdTests
{
    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    static GameObject Wall(float x, float width = 0.1f)
    {
        var w = new GameObject("Wall"); w.transform.position = new Vector3(x, 0, 0);
        w.AddComponent<BoxCollider2D>().size = new Vector2(width, 4);
        return w;
    }

    static Rigidbody2D Bullet(CollisionDetectionMode2D mode, float speed)
    {
        var g = new GameObject("Bullet"); g.transform.position = Vector3.zero;
        g.AddComponent<CircleCollider2D>().radius = 0.1f;
        var rb = g.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0; rb.collisionDetectionMode = mode; rb.velocity = new Vector2(speed, 0);
        return rb;
    }

    [UnityTest] public IEnumerator DiscreteBodyTunnelsThroughThinWall()
    {
        Wall(5);
        var rb = Bullet(CollisionDetectionMode2D.Discrete, 200);   // 4 units per 0.02 s step
        yield return Fixed(5);
        Assert.Greater(rb.position.x, 6, "a discrete body skips over a wall thinner than its step");
    }

    [UnityTest] public IEnumerator ContinuousBodyStopsAtThinWall()
    {
        Wall(5);
        var rb = Bullet(CollisionDetectionMode2D.Continuous, 200);
        int hits = 0;
        rb.gameObject.AddComponent<HitCounter>().onHit = () => hits++;
        yield return Fixed(5);
        Assert.Less(rb.position.x, 5, "continuous collision stops the bullet in front of the wall");
        Assert.AreEqual(4.85f, rb.position.x, 0.06f, "resting against the wall face (radius 0.1 + half width 0.05)");
        Assert.Greater(hits, 0, "OnCollisionEnter2D reported");
    }

    [UnityTest] public IEnumerator ContinuousBodyHitsFastThinDynamicPlank()
    {
        var plank = new GameObject("Plank"); plank.transform.position = new Vector3(5, 0, 0);
        plank.AddComponent<BoxCollider2D>().size = new Vector2(0.1f, 2);
        var prb = plank.AddComponent<Rigidbody2D>(); prb.gravityScale = 0;
        var rb = Bullet(CollisionDetectionMode2D.Continuous, 200);
        yield return Fixed(5);
        Assert.Greater(prb.velocity.x, 1, "the plank is pushed by the bullet");
        Assert.Less(rb.position.x, prb.position.x, "the bullet stays behind the plank");
    }

    [UnityTest] public IEnumerator HingeMotorPositiveSpeedTurnsCounterClockwise()
    {
        var g = new GameObject("Rotor"); g.AddComponent<BoxCollider2D>().size = new Vector2(2, 0.2f);
        var rb = g.AddComponent<Rigidbody2D>(); rb.gravityScale = 0;
        var h = g.AddComponent<HingeJoint2D>();
        h.useMotor = true; h.motor = new JointMotor2D { motorSpeed = 90, maxMotorTorque = 10000 };
        yield return Fixed(50);
        Assert.AreEqual(90, rb.angularVelocity, 1f, "motorSpeed is degrees per second, positive = counter-clockwise");
        Assert.AreEqual(90, h.jointSpeed, 1f);
        Assert.Greater(rb.rotation, 80);
    }

    class HitCounter : MonoBehaviour
    {
        public System.Action onHit;
        void OnCollisionEnter2D(Collision2D c) => onHit?.Invoke();
    }
}
