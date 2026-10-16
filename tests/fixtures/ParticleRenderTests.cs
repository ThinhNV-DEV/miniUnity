using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Particle shape module (start offset / direction), EmitParams.applyShapeToPosition, color / size / velocity over lifetime,
// Gradient, and particles drawn by Camera.Render as camera-facing quads.
public class ParticleRenderTests
{
    static ParticleSystem System(string name = "PS")
    {
        var ps = new GameObject(name).AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.startLifetime = 2f; main.startSpeed = 5f; main.startSize = 0.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.enabled = false;
        return ps;
    }

    static ParticleSystem.Particle[] All(ParticleSystem ps) { var a = new ParticleSystem.Particle[ps.particleCount]; ps.GetParticles(a); return a; }

    [Test] public void SphereShapeSpreadsDirectionsAndPositions()
    {
        var ps = System();
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2;
        ps.Emit(400);
        var mean = Vector3.zero; float maxR = 0;
        foreach (var p in All(ps)) { mean += p.velocity.normalized; maxR = Mathf.Max(maxR, p.position.magnitude); Assert.AreEqual(5, p.velocity.magnitude, 1e-3f); }
        Assert.Less((mean / 400).magnitude, 0.15f, "directions all around");
        Assert.LessOrEqual(maxR, 2.0001f); Assert.Greater(maxR, 1.5f, "inside the sphere, out to its radius");
        foreach (var p in All(ps)) if (p.position.sqrMagnitude > 1e-6f) Assert.Greater(Vector3.Dot(p.position.normalized, p.velocity.normalized), 0.999f, "moving outward");
    }

    [Test] public void ConeShapeKeepsWithinTheAngle()
    {
        var ps = System();
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 30; sh.radius = 1;
        ps.Emit(200);
        foreach (var p in All(ps)) Assert.LessOrEqual(Vector3.Angle(Vector3.forward, p.velocity), 30.01f);
    }

    [Test] public void ApplyShapeToPositionAddsTheShapeOffset()
    {
        var ps = System();
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;
        var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true, position = new Vector3(5, 5, 0) };
        ps.Emit(ep, 50);
        foreach (var p in All(ps)) Assert.LessOrEqual(Vector3.Distance(p.position, new Vector3(5, 5, 0)), 0.1001f);
        var spread = All(ps)[0].velocity.normalized - All(ps)[1].velocity.normalized;
        Assert.Greater(spread.magnitude, 1e-3f, "velocity follows the shape too");
    }

    [Test] public void GradientEvaluatesLikeUnity()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.blue, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        var mid = g.Evaluate(0.5f);
        Assert.AreEqual(0.5f, mid.r, 1e-4f); Assert.AreEqual(0.5f, mid.b, 1e-4f); Assert.AreEqual(0.5f, mid.a, 1e-4f);
        Assert.AreEqual(Color.red, g.Evaluate(-1));
        g.mode = GradientMode.Fixed;
        Assert.AreEqual(Color.blue.b, g.Evaluate(0.25f).b, "Fixed holds the next key");
        Assert.AreEqual(2, g.colorKeys.Length);
        var k = new GradientColorKey(Color.green, 0.3f);
        Assert.AreEqual(0.3f, k.time); Assert.AreEqual(Color.green, k.color);
    }

    [UnityTest] public IEnumerator OverLifetimeModules()
    {
        var ps = System();
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.black, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.y = 2f;
        var main = ps.main; main.startSpeed = 0f;
        ps.Play();
        ps.Emit(1);
        float t0 = Time.time;
        while (Time.time - t0 < 1f) yield return null;
        var p = All(ps)[0];
        float age = 1 - p.remainingLifetime / p.startLifetime;
        Color c = p.GetCurrentColor(ps);
        Assert.AreEqual(1 - age, c.r, 0.02f, "white → black over the lifetime");
        Assert.AreEqual(0.5f * (1 - age), p.GetCurrentSize(ps), 0.02f, "start size × size curve");
        Assert.AreEqual(2f * (2 - p.remainingLifetime), p.position.y, 0.1f, "velocity over lifetime moves it up");
    }

    [Test] public void ParticlesAreDrawnAsCameraFacingQuads()
    {
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 1; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        var rt = new RenderTexture(32, 32, 24); cam.targetTexture = rt;
        var ps = System();
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        var ep = new ParticleSystem.EmitParams { position = Vector3.zero, velocity = Vector3.zero, startSize = 0.5f, startColor = Color.green };
        ps.Emit(ep, 1);
        cam.Render();
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); tex.Apply(); RenderTexture.active = null;
        Color32 mid = tex.GetPixel(16, 16), edge = tex.GetPixel(2, 2);
        Assert.AreEqual(255, mid.g); Assert.AreEqual(0, mid.r);
        Assert.AreEqual(0, edge.g, "a 0.5-unit quad covers the middle quarter only");
    }
}
