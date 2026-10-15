using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ParticleTests
{
    static ParticleSystem NewSystem(string name = "PS")
    {
        var ps = new GameObject(name).AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        return ps;
    }

    [Test] public void EmitParamsOverrideMainDefaultsAndGetParticlesCopiesThem()
    {
        var ps = NewSystem();
        var main = ps.main;
        main.startSize = 2f; main.startLifetime = 3f; main.startColor = Color.red; main.maxParticles = 5;
        ps.Emit(2);
        var ep = new ParticleSystem.EmitParams { position = new Vector3(1, 2, 3), startSize = 0.25f };
        ps.Emit(ep, 10);                                   // capped by maxParticles
        Assert.AreEqual(5, ps.particleCount);
        var buf = new ParticleSystem.Particle[8];
        Assert.AreEqual(5, ps.GetParticles(buf));
        Assert.AreEqual(2f, buf[0].startSize); Assert.AreEqual(3f, buf[0].remainingLifetime);
        Assert.AreEqual((Color32)Color.red, buf[0].startColor);
        Assert.AreEqual(0.25f, buf[4].startSize); Assert.AreEqual(new Vector3(1, 2, 3), buf[4].position);
        ps.Clear();
        Assert.AreEqual(0, ps.particleCount);
    }

    [Test] public void MinMaxCurveModesAndMultipliers()
    {
        ParticleSystem.MinMaxCurve c = 4f;
        Assert.AreEqual(ParticleSystemCurveMode.Constant, c.mode); Assert.AreEqual(4f, c.Evaluate(0.3f));
        var two = new ParticleSystem.MinMaxCurve(2f, 6f);
        Assert.AreEqual(4f, two.Evaluate(0, 0.5f));
        var curve = new ParticleSystem.MinMaxCurve(10f, AnimationCurve.Linear(0, 0, 1, 1));
        Assert.AreEqual(5f, curve.Evaluate(0.5f), 1e-4f);
        var ps = NewSystem(); var main = ps.main;
        main.startSpeedMultiplier = 7f;
        Assert.AreEqual(7f, main.startSpeed.constant);
    }

    [UnityTest] public IEnumerator RateOverTimeEmitsAndParticlesDieAtTheirLifetime()
    {
        var ps = NewSystem();
        var main = ps.main; main.startLifetime = 0.5f; main.startSpeed = 2f; main.duration = 10f;
        var em = ps.emission; em.rateOverTime = 20f;
        ps.Play();
        for (int i = 0; i < 15; i++) yield return null;     // 0.25 s at 60 fps
        Assert.That(ps.particleCount, Is.InRange(4, 6), "20/s for 0.25 s");
        var buf = new ParticleSystem.Particle[16]; ps.GetParticles(buf);
        Assert.Greater(buf[0].position.z, 0.1f, "particles move along the emitter's forward axis");
        em.enabled = false;
        for (int i = 0; i < 40; i++) yield return null;     // longer than the lifetime
        Assert.AreEqual(0, ps.particleCount);
    }

    [UnityTest] public IEnumerator BurstThenStopActionDestroysTheObject()
    {
        var ps = NewSystem("Burst");
        var main = ps.main; main.loop = false; main.duration = 0.1f; main.startLifetime = 0.2f;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var em = ps.emission; em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
        ps.Play();
        yield return null;
        Assert.AreEqual(12, ps.particleCount);
        Assert.IsTrue(ps.isPlaying);
        for (int i = 0; i < 30; i++) yield return null;
        Assert.IsTrue(ps == null, "stopAction Destroy removes the GameObject once the system has stopped");
    }

    [UnityTest] public IEnumerator PlayOnAwakeAndSimulate()
    {
        var auto = new GameObject("Auto").AddComponent<ParticleSystem>();   // playOnAwake defaults to true
        yield return null;
        Assert.IsTrue(auto.isPlaying);
        var ps = NewSystem();
        var em = ps.emission; em.rateOverTime = 10f;
        ps.Simulate(1f);
        Assert.IsTrue(ps.isPaused);
        Assert.That(ps.particleCount, Is.InRange(9, 11));
    }
}
