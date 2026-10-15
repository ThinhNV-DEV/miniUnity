using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class WheelTests
{
    static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    static (Rigidbody body, WheelCollider[] wheels) Car()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Ground";
        ground.transform.position = new Vector3(0, -0.5f, 0); ground.transform.localScale = new Vector3(400, 1, 400);
        var car = new GameObject("Car"); car.transform.position = new Vector3(0, 1.0f, 0);
        var box = car.AddComponent<BoxCollider>(); box.size = new Vector3(2, 0.5f, 4); box.center = new Vector3(0, 0.3f, 0);
        var rb = car.AddComponent<Rigidbody>(); rb.mass = 1000; rb.centerOfMass = new Vector3(0, -0.2f, 0);
        var wheels = new WheelCollider[4];
        int i = 0;
        foreach (var z in new[] { 1.4f, -1.4f })
            foreach (var x in new[] { -0.9f, 0.9f })
            {
                var w = new GameObject("Wheel" + i); w.transform.SetParent(car.transform, false); w.transform.localPosition = new Vector3(x, 0, z);
                var wc = w.AddComponent<WheelCollider>(); wc.radius = 0.35f; wc.suspensionDistance = 0.3f;
                wheels[i++] = wc;
            }
        return (rb, wheels);
    }

    [UnityTest] public IEnumerator CarSettlesOnItsSuspension()
    {
        var (rb, wheels) = Car();
        yield return Fixed(150);
        foreach (var w in wheels) Assert.IsTrue(w.isGrounded, w.name + " touches the ground");
        Assert.Less(rb.velocity.magnitude, 0.05f, "at rest");
        Assert.IsTrue(wheels[0].GetGroundHit(out var hit));
        Assert.AreEqual(rb.mass * 9.81f / 4, hit.force, 250f, "each wheel carries a quarter of the weight");
        wheels[0].GetWorldPose(out var pos, out _);
        Assert.AreEqual(0.35f, pos.y, 0.03f, "wheel centre one radius above the ground");
        Assert.AreEqual(0.35f + 0.3f * 0.5f, rb.position.y, 0.05f, "suspension rests at the target position (half travel)");
    }

    [UnityTest] public IEnumerator MotorTorqueDrivesForwardAndBrakesStop()
    {
        var (rb, wheels) = Car();
        yield return Fixed(100);
        foreach (var w in wheels) w.motorTorque = 400;
        yield return Fixed(150);
        Assert.Greater(Vector3.Dot(rb.velocity, rb.transform.forward), 3f, "accelerates along +Z");
        Assert.Greater(wheels[0].rpm, 50f, "wheels spin forward");
        Assert.AreEqual(0, rb.velocity.x, 0.2f, "no sideways drift");
        foreach (var w in wheels) { w.motorTorque = 0; w.brakeTorque = 3000; }
        yield return Fixed(250);
        Assert.Less(rb.velocity.magnitude, 0.2f, "brakes stop the car");
        Assert.AreEqual(0, wheels[0].rpm, 1f);
    }

    [UnityTest] public IEnumerator SteeringTurnsTheCar()
    {
        var (rb, wheels) = Car();
        yield return Fixed(100);
        foreach (var w in wheels) w.motorTorque = 300;
        wheels[0].steerAngle = wheels[1].steerAngle = 20;
        yield return Fixed(200);
        Assert.Greater(rb.transform.eulerAngles.y, 10f, "positive steer angle turns right (clockwise seen from above)");
        Assert.Less(rb.transform.eulerAngles.y, 180f);
    }
}
