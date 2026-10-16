using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Questions only the real engine can answer. Every value is reported with Oracle.Report; nothing is asserted here, the comparison
// is done by run_oracle.py against Unity's committed values. Fixed timestep 0.02 and default physics settings.
public class OracleTests
{
    static IEnumerator FixedSteps(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

    [UnityTest] public IEnumerator HingeJoint2DMotorDirection()
    {
        var anchor = new GameObject("Anchor").AddComponent<Rigidbody2D>(); anchor.bodyType = RigidbodyType2D.Static;
        var arm = new GameObject("Arm"); arm.transform.position = new Vector3(1, 0, 0);
        var rb = arm.AddComponent<Rigidbody2D>(); rb.gravityScale = 0;
        arm.AddComponent<BoxCollider2D>().size = new Vector2(2, 0.2f);
        var hinge = arm.AddComponent<HingeJoint2D>();
        hinge.connectedBody = anchor; hinge.anchor = new Vector2(-1, 0); hinge.autoConfigureConnectedAnchor = true;
        hinge.useMotor = true; hinge.motor = new JointMotor2D { motorSpeed = 90, maxMotorTorque = 10000 };
        yield return FixedSteps(50);
        Oracle.Report("hinge2d.motor+90.rotationAfter1s", rb.rotation, 5);
        Oracle.Report("hinge2d.motor+90.jointAngle", hinge.jointAngle, 5);
        Oracle.Report("hinge2d.motor+90.jointSpeed", hinge.jointSpeed, 5);
    }

    [UnityTest] public IEnumerator FreeFall()
    {
        var b3 = GameObject.CreatePrimitive(PrimitiveType.Sphere).AddComponent<Rigidbody>();
        var b2 = new GameObject("B2").AddComponent<Rigidbody2D>();
        yield return FixedSteps(50);
        Oracle.Report("fall3d.yAfter50Steps", b3.position.y, 1e-3);
        Oracle.Report("fall3d.vyAfter50Steps", b3.velocity.y, 1e-3);
        Oracle.Report("fall2d.yAfter50Steps", b2.position.y, 1e-3);
    }

    [UnityTest] public IEnumerator BounceAndRest()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.localScale = new Vector3(20, 1, 20); floor.transform.position = new Vector3(0, -0.5f, 0);
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); ball.transform.position = new Vector3(0, 3, 0);
        var mat = new PhysicMaterial { bounciness = 0.8f, bounceCombine = PhysicMaterialCombine.Maximum, dynamicFriction = 0, staticFriction = 0 };
        ball.GetComponent<Collider>().sharedMaterial = mat;
        var rb = ball.AddComponent<Rigidbody>();
        float peak = 0; bool bounced = false;
        for (int i = 0; i < 150; i++)
        {
            yield return new WaitForFixedUpdate();
            if (rb.velocity.y > 0) bounced = true;
            if (bounced) peak = Mathf.Max(peak, rb.position.y);
        }
        Oracle.Report("bounce3d.firstPeak", peak, 0.1);
        Oracle.Report("bounce3d.yAfter3s", rb.position.y, 0.1);
    }

    [UnityTest] public IEnumerator BoxSlidesDownSlope()
    {
        var slope = GameObject.CreatePrimitive(PrimitiveType.Cube); slope.transform.localScale = new Vector3(40, 1, 4); slope.transform.rotation = Quaternion.Euler(0, 0, -30);
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.transform.rotation = Quaternion.Euler(0, 0, -30); box.transform.position = slope.transform.TransformPoint(new Vector3(0, 1f, 0));
        var mat = new PhysicMaterial { dynamicFriction = 0.3f, staticFriction = 0.3f, frictionCombine = PhysicMaterialCombine.Average };
        box.GetComponent<Collider>().sharedMaterial = mat; slope.GetComponent<Collider>().sharedMaterial = mat;
        var rb = box.AddComponent<Rigidbody>();
        var start = rb.position;
        yield return FixedSteps(50);
        Oracle.Report("slope3d.distanceAfter1s", Vector3.Distance(start, rb.position), 0.05);
    }

    [UnityTest] public IEnumerator StackOfThreeBoxes()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.localScale = new Vector3(20, 1, 20); floor.transform.position = new Vector3(0, -0.5f, 0);
        Rigidbody top = null;
        for (int i = 0; i < 3; i++)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube); b.transform.position = new Vector3(0.05f * i, 0.5f + i, 0);
            top = b.AddComponent<Rigidbody>();
        }
        yield return FixedSteps(100);
        Oracle.Report("stack3d.topAfter2s", top.position, 0.02);
    }

    [Test] public void RandomSequence()
    {
        Random.InitState(42);
        Oracle.Report("random.seed42.value0", Random.value, 1e-7);
        Oracle.Report("random.seed42.value1", Random.value, 1e-7);
        Oracle.Report("random.seed42.range0_100", Random.Range(0, 100), 0);
        Oracle.Report("random.seed42.insideUnitSphere", Random.insideUnitSphere, 1e-6);
        Oracle.Report("perlin(0.3,0.7)", Mathf.PerlinNoise(0.3f, 0.7f), 1e-6);
        Oracle.Report("perlin(12.25,-3.5)", Mathf.PerlinNoise(12.25f, -3.5f), 1e-6);
    }

    [Test] public void QuaternionAndEuler()
    {
        var q = Quaternion.Euler(30, 200, -45);
        Oracle.Report("euler(30,200,-45).eulerAngles", q.eulerAngles, 1e-3);
        Oracle.Report("euler(90,10,0).eulerAngles", Quaternion.Euler(90, 10, 0).eulerAngles, 1e-2);
        Oracle.Report("slerp.angle", Quaternion.Angle(Quaternion.identity, Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0, 170, 0), 0.3f)), 1e-3);
        Oracle.Report("lookRotation(up,fwd).eulerAngles", Quaternion.LookRotation(Vector3.up, Vector3.forward).eulerAngles, 1e-3);
    }

    [Test] public void SmoothDampAndMoveTowards()
    {
        float v = 0, x = 0;
        for (int i = 0; i < 30; i++) x = Mathf.SmoothDamp(x, 10, ref v, 0.3f, Mathf.Infinity, 1 / 60f);
        Oracle.Report("smoothDamp.after30", x, 1e-4);
        Oracle.Report("smoothDampAngle", Mathf.SmoothDampAngle(350, 10, ref v, 0.2f, 100, 0.1f), 1e-4);
        Oracle.Report("deltaAngle(350,10)", Mathf.DeltaAngle(350, 10), 0);
    }

    [UnityTest] public IEnumerator CharacterControllerSlidesAlongWall()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.localScale = new Vector3(1, 4, 10); wall.transform.position = new Vector3(2, 0, 0);
        var cc = new GameObject("CC").AddComponent<CharacterController>();
        for (int i = 0; i < 30; i++) { cc.Move(new Vector3(0.2f, 0, 0.1f)); yield return null; }
        Oracle.Report("cc.slide.position", cc.transform.position, 0.02);
    }

    // Default-Skybox (Skybox/Procedural) with a default-like sun: sky clear, sky ambient, default reflection (sRGB 0..1 values)
    [Test] public void DefaultSkyboxAmbientAndReflection()
    {
        const int N = 64;
        var rt = new RenderTexture(N, N, 24);
        var cam = new GameObject("SkyCam").AddComponent<Camera>();
        cam.fieldOfView = 90; cam.clearFlags = CameraClearFlags.Skybox; cam.targetTexture = rt;
        var sun = new GameObject("SkySun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1; sun.color = new Color(1, .957f, .839f);
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);
        Texture2D Grab()
        {
            cam.Render();
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            RenderTexture.active = rt; t.ReadPixels(new Rect(0, 0, N, N), 0, 0); t.Apply(); RenderTexture.active = null;
            return t;
        }
        void Rgb(string key, Color c) { Oracle.Report(key + ".r", c.r, 0.03); Oracle.Report(key + ".g", c.g, 0.03); Oracle.Report(key + ".b", c.b, 0.03); }
        var sky = Grab();
        Rgb("sky.up45", sky.GetPixel(N / 2, N - 2)); Rgb("sky.up10", sky.GetPixel(N / 2, N / 2 + 6)); Rgb("sky.ground", sky.GetPixel(N / 2, 4));
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); ball.transform.position = new Vector3(0, 0, 2);
        var m = new Material(Shader.Find("Standard")) { color = Color.white };
        ball.GetComponent<Renderer>().sharedMaterial = m;
        cam.fieldOfView = 40; sun.enabled = false;
        m.SetFloat("_Metallic", 0); m.SetFloat("_Glossiness", 0);
        var matte = Grab();
        Rgb("ambient.ball.top", matte.GetPixel(N / 2, N / 2 + 18)); Rgb("ambient.ball.bottom", matte.GetPixel(N / 2, N / 2 - 18));
        m.SetFloat("_Metallic", 1); m.SetFloat("_Glossiness", 1);
        var mirror = Grab();
        Rgb("reflection.mirror.top", mirror.GetPixel(N / 2, N / 2 + 18)); Rgb("reflection.mirror.center", mirror.GetPixel(N / 2, N / 2));
        m.SetFloat("_Glossiness", 0.5f);
        Rgb("reflection.rough.top", Grab().GetPixel(N / 2, N / 2 + 18));
        Object.DestroyImmediate(ball); Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(sun.gameObject); rt.Release();
    }
}
