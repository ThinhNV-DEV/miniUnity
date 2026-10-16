// ParticleSystem without a renderer: the particle state a script can see and change.
// - main / emission modules (all properties), Emit (count / EmitParams / Particle), Get/SetParticles, Play / Pause / Stop / Clear /
//   Simulate / IsAlive, isPlaying / isEmitting / isStopped / isPaused, particleCount, time.
// - In Play mode each system is simulated once per frame before LateUpdate (Unity: PreLateUpdate.ParticleSystemBeginUpdateAll):
//   lifetime, velocity, gravity modifier, angular velocity, emission rate over time, bursts, duration / looping, stop action,
//   play on awake.
// - Initial settings of a system loaded from a scene / prefab come from its serialized modules (constant values; a curve is
//   read as its scalar). Values set from script live in memory and are not written back to the asset.
// Shape, Color / Size / Rotation / Velocity over lifetime: ParticleModules.cs; drawn by UIRenderer.DrawParticles (billboards).
// Not simulated: noise, collision, trigger, sub emitters, trails, lights, texture sheet animation, limit velocity, force.
using System;
using System.Collections.Generic;
using MiniUnity.Yaml;
using Unity.Collections;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        /// <summary>Settings + live particles of one system (created on first use).</summary>
        internal sealed class State
        {
            // main
            public float duration = 5, simulationSpeed = 1, flipRotation, randomizeRotationDirection;
            public bool loop = true, prewarm, playOnAwake = true, useUnscaledTime, startSize3D, startRotation3D, autoRandomSeed = true;
            public MinMaxCurve startDelay = 0f, startLifetime = 5f, startSpeed = 5f, startSize = 1f, startSizeY = 1f, startSizeZ = 1f,
                               startRotation = 0f, startRotationX = 0f, startRotationY = 0f, gravityModifier = 0f;
            public MinMaxGradient startColor = Color.white;
            public ParticleSystemGravitySource gravitySource;
            public ParticleSystemSimulationSpace simulationSpace = ParticleSystemSimulationSpace.Local;
            public Transform customSimulationSpace;
            public ParticleSystemScalingMode scalingMode = ParticleSystemScalingMode.Local;
            public int maxParticles = 1000;
            public ParticleSystemEmitterVelocityMode emitterVelocityMode = ParticleSystemEmitterVelocityMode.Rigidbody;
            public Vector3 emitterVelocity;
            public ParticleSystemStopAction stopAction;
            public ParticleSystemRingBufferMode ringBufferMode;
            public Vector2 ringBufferLoopRange = new Vector2(0, 1);
            public ParticleSystemCullingMode cullingMode;
            // emission
            public bool emissionEnabled = true;
            public MinMaxCurve rateOverTime = 10f, rateOverDistance = 0f;
            public List<Burst> bursts = new();
            // playback
            public readonly List<Particle> particles = new();
            public bool playing, emitting, paused, awoken;
            public float time, emitDebt;
            public uint randomSeed;
            public System.Random random = new(0);
            public float Random01() => (float)random.NextDouble();
        }

        State m_State;
        internal State S => m_State ??= LoadState();

        State LoadState()
        {
            var s = new State();
            if (m_Native != null)
            {
                s.duration = NFloat("lengthInSec", 5); s.simulationSpeed = NFloat("simulationSpeed", 1);
                s.loop = NBool("looping", true); s.prewarm = NBool("prewarm"); s.playOnAwake = NBool("playOnAwake", true);
                s.useUnscaledTime = NBool("useUnscaledTime"); s.autoRandomSeed = NBool("autoRandomSeed", true); s.randomSeed = (uint)NLong("randomSeed");
                s.simulationSpace = (ParticleSystemSimulationSpace)NInt("moveWithTransform");
                s.scalingMode = (ParticleSystemScalingMode)NInt("scalingMode", 1);
                s.stopAction = (ParticleSystemStopAction)NInt("stopAction"); s.cullingMode = (ParticleSystemCullingMode)NInt("cullingMode");
                s.ringBufferMode = (ParticleSystemRingBufferMode)NInt("ringBufferMode");
                s.startDelay = CurveOf(NGet("startDelay"), 0);
                s.startLifetime = CurveOf(NGet("InitialModule.startLifetime"), 5); s.startSpeed = CurveOf(NGet("InitialModule.startSpeed"), 5);
                s.startSize = CurveOf(NGet("InitialModule.startSize"), 1); s.startSizeY = CurveOf(NGet("InitialModule.startSizeY"), 1);
                s.startSizeZ = CurveOf(NGet("InitialModule.startSizeZ"), 1); s.startSize3D = NBool("InitialModule.size3D");
                s.startRotation = CurveOf(NGet("InitialModule.startRotation"), 0); s.startRotation3D = NBool("InitialModule.rotation3D");
                s.gravityModifier = CurveOf(NGet("InitialModule.gravityModifier"), 0);
                s.startColor = GradientOf(NGet("InitialModule.startColor"));
                s.maxParticles = NInt("InitialModule.maxNumParticles", 1000);
                s.emissionEnabled = NBool("EmissionModule.enabled", true);
                s.rateOverTime = CurveOf(NGet("EmissionModule.rateOverTime"), 10); s.rateOverDistance = CurveOf(NGet("EmissionModule.rateOverDistance"), 0);
                if (NGet("EmissionModule.m_Bursts") is YSeq bs)
                    foreach (var b in bs.Items)
                        if (b is YMap bm)
                            s.bursts.Add(new Burst(F(bm["time"]), CurveOf(bm["countCurve"], 30), (int)F(bm["cycleCount"], 1), F(bm["repeatInterval"], 0.01f)) { probability = F(bm["probability"], 1) });
            }
            // Unity picks a new seed every play for auto-seeded systems; MiniUnity derives it from MU_RANDOM_SEED and the object's name
            // so a run can be repeated exactly
            s.random = new System.Random(s.autoRandomSeed ? AutoSeed() : (int)s.randomSeed);
            return s;
        }

        int AutoSeed()
        {
            int.TryParse(Environment.GetEnvironmentVariable("MU_RANDOM_SEED"), out int baseSeed);
            uint h = 2166136261u;
            foreach (char ch in m_GameObject != null ? m_GameObject.m_Name : "") h = (h ^ ch) * 16777619u;
            return baseSeed ^ (int)h;
        }

        static float F(YNode n, float def = 0) => n is YScalar v && v.Value.Length > 0 ? v.F : def;
        /// <summary>Serialized MinMaxCurve (minMaxState 0 constant, 1 curve, 2 two curves, 3 two constants); curves read as their scalar.</summary>
        static MinMaxCurve CurveOf(YNode n, float def)
        {
            if (n is not YMap m) return def;
            float max = F(m["scalar"], def), min = F(m["minScalar"], max);
            return (int)F(m["minMaxState"]) switch
            {
                3 => new MinMaxCurve(min, max),
                1 => new MinMaxCurve(max, MiniUnity.Serializer.ReadCurve(m["maxCurve"])),
                2 => new MinMaxCurve(max, MiniUnity.Serializer.ReadCurve(m["minCurve"]), MiniUnity.Serializer.ReadCurve(m["maxCurve"])),
                _ => new MinMaxCurve(max),
            };
        }
        static Color ColorOf(YNode n, Color def) => n is YMap m ? new Color(F(m["r"], 1), F(m["g"], 1), F(m["b"], 1), F(m["a"], 1)) : def;
        /// <summary>Serialized MinMaxGradient (minMaxState 0 color, 2 two colors, gradients read as their max color).</summary>
        static MinMaxGradient GradientOf(YNode n)
        {
            if (n is not YMap m) return Color.white;
            var max = ColorOf(m["maxColor"], Color.white);
            switch ((int)F(m["minMaxState"]))
            {
                case 1: return new MinMaxGradient(Gradient.FromYaml(m["maxGradient"]));
                case 2: return new MinMaxGradient(ColorOf(m["minColor"], Color.white), max);
                case 3: return new MinMaxGradient(Gradient.FromYaml(m["minGradient"]), Gradient.FromYaml(m["maxGradient"]));
                case 4: { var g = new MinMaxGradient(Gradient.FromYaml(m["maxGradient"])); g.mode = ParticleSystemGradientMode.RandomColor; return g; }
                default: return new MinMaxGradient(max);
            }
        }

        // ------------------------------------------------------------------ modules
        public MainModule main => new MainModule(this);
        public EmissionModule emission => new EmissionModule(this);

        // ------------------------------------------------------------------ playback state
        public bool isPlaying => S.playing && !S.paused;
        public bool isEmitting => S.playing && S.emitting && !S.paused;
        public bool isStopped => !S.playing && !S.paused;
        public bool isPaused => S.paused;
        public int particleCount => S.particles.Count;
        public float time { get => S.time; set => S.time = Mathf.Max(0, value); }
        public float totalTime => S.time;
        public uint randomSeed { get => S.randomSeed; set { S.randomSeed = value; S.random = new System.Random((int)value); } }
        public bool useAutoRandomSeed { get => S.autoRandomSeed; set => S.autoRandomSeed = value; }

        IEnumerable<ParticleSystem> Systems(bool withChildren)
        {
            if (!withChildren) { yield return this; yield break; }
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true)) yield return ps;
        }

        public void Play() => Play(true);
        public void Play(bool withChildren) { foreach (var ps in Systems(withChildren)) ps.PlayOne(); }
        void PlayOne()
        {
            var s = S;
            s.awoken = true;
            if (s.paused) { s.paused = false; return; }
            if (!s.playing) { s.time = 0; s.emitDebt = 0; }
            s.playing = true; s.emitting = true;
        }
        public void Pause() => Pause(true);
        public void Pause(bool withChildren) { foreach (var ps in Systems(withChildren)) if (ps.S.playing) ps.S.paused = true; }
        public void Stop() => Stop(true, ParticleSystemStopBehavior.StopEmitting);
        public void Stop(bool withChildren) => Stop(withChildren, ParticleSystemStopBehavior.StopEmitting);
        public void Stop(bool withChildren, ParticleSystemStopBehavior stopBehavior)
        {
            foreach (var ps in Systems(withChildren))
            {
                var s = ps.S;
                s.emitting = false; s.paused = false;
                if (stopBehavior == ParticleSystemStopBehavior.StopEmittingAndClear) { s.particles.Clear(); s.playing = false; s.time = 0; }
                else if (s.particles.Count == 0) { s.playing = false; s.time = 0; }
            }
        }
        public void Clear() => Clear(true);
        public void Clear(bool withChildren) { foreach (var ps in Systems(withChildren)) ps.S.particles.Clear(); }
        public bool IsAlive() => IsAlive(true);
        public bool IsAlive(bool withChildren)
        {
            foreach (var ps in Systems(withChildren)) if (ps.S.particles.Count > 0 || (ps.S.playing && ps.S.emitting)) return true;
            return false;
        }

        public void Simulate(float t) => Simulate(t, true, true, true);
        public void Simulate(float t, bool withChildren) => Simulate(t, withChildren, true, true);
        public void Simulate(float t, bool withChildren, bool restart) => Simulate(t, withChildren, restart, true);
        /// <summary>Fast-forward by <paramref name="t"/> seconds and leave the system paused (as Unity does).</summary>
        public void Simulate(float t, bool withChildren, bool restart, bool fixedTimeStep)
        {
            foreach (var ps in Systems(withChildren))
            {
                var s = ps.S;
                if (restart) { s.particles.Clear(); s.time = 0; s.emitDebt = 0; }
                s.playing = true; s.emitting = true; s.paused = false;
                float step = fixedTimeStep ? Mathf.Max(1e-4f, Time.fixedDeltaTime) : t;
                for (float done = 0; done < t - 1e-6f; done += step) ps.Advance(Mathf.Min(step, t - done));
                s.paused = s.playing;
            }
        }

        // ------------------------------------------------------------------ emission
        public void Emit(int count) { for (int i = 0; i < count; i++) if (!Add(NewParticle())) break; }
        public void Emit(EmitParams emitParams, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var p = NewParticle();
                emitParams.ApplyTo(ref p, EmitterPosition());
                if (!Add(p)) break;
            }
        }
        public void Emit(Particle particle) => Add(particle);
        public void Emit(Vector3 position, Vector3 velocity, float size, float lifetime, Color32 color)
        {
            var p = NewParticle();
            p.position = position; p.velocity = velocity; p.startSize = size; p.startLifetime = p.remainingLifetime = lifetime; p.startColor = color;
            Add(p);
        }

        bool Add(Particle p)
        {
            var s = S;
            if (s.particles.Count >= s.maxParticles) return false;
            s.particles.Add(p);
            return true;
        }

        /// <summary>Emitter origin in simulation space.</summary>
        Vector3 EmitterPosition() => S.simulationSpace == ParticleSystemSimulationSpace.Local ? Vector3.zero : transform.position;

        Particle NewParticle()
        {
            var s = S;
            float u = s.duration > 0 ? Mathf.Clamp01(s.time / s.duration) : 0;
            var p = new Particle();
            float life = s.startLifetime.Evaluate(u, s.Random01());
            p.startLifetime = life; p.remainingLifetime = life;
            // the shape module gives the start offset and direction (emitter space)
            ShapeSample(out var lp, out var ldir);
            bool local = s.simulationSpace == ParticleSystemSimulationSpace.Local;
            var dir = local ? ldir : transform.rotation * ldir;
            p.velocity = dir * s.startSpeed.Evaluate(u, s.Random01());
            p.position = local ? lp : transform.position + transform.rotation * lp;
            float size = s.startSize.Evaluate(u, s.Random01());
            p.startSize3D = s.startSize3D ? new Vector3(size, s.startSizeY.Evaluate(u, s.Random01()), s.startSizeZ.Evaluate(u, s.Random01())) : new Vector3(size, size, size);
            p.rotation = s.startRotation.Evaluate(u, s.Random01()) * Mathf.Rad2Deg;
            p.startColor = s.startColor.Evaluate(u, s.Random01());
            p.randomSeed = (uint)s.random.Next();
            p.axisOfRotation = Vector3.forward;
            return p;
        }

        // ------------------------------------------------------------------ particle access
        public int GetParticles(Particle[] particles) => GetParticles(particles, -1, 0);
        public int GetParticles(Particle[] particles, int size) => GetParticles(particles, size, 0);
        public int GetParticles(Particle[] particles, int size, int offset)
        {
            if (particles == null) throw new ArgumentNullException(nameof(particles));
            var list = S.particles;
            int n = Math.Max(0, Math.Min(size < 0 ? particles.Length : Math.Min(size, particles.Length), list.Count - offset));
            for (int i = 0; i < n; i++) particles[i] = list[offset + i];
            return n;
        }
        public int GetParticles(NativeArray<Particle> particles) => GetParticles(particles, -1, 0);
        public int GetParticles(NativeArray<Particle> particles, int size) => GetParticles(particles, size, 0);
        public int GetParticles(NativeArray<Particle> particles, int size, int offset)
        {
            var list = S.particles;
            int n = Math.Max(0, Math.Min(size < 0 ? particles.Length : Math.Min(size, particles.Length), list.Count - offset));
            for (int i = 0; i < n; i++) particles[i] = list[offset + i];
            return n;
        }
        public void SetParticles(Particle[] particles) => SetParticles(particles, -1, 0);
        public void SetParticles(Particle[] particles, int size) => SetParticles(particles, size, 0);
        public void SetParticles(Particle[] particles, int size, int offset)
        {
            if (particles == null) throw new ArgumentNullException(nameof(particles));
            SetFrom(i => particles[i], size < 0 ? particles.Length : Math.Min(size, particles.Length), offset);
        }
        public void SetParticles(NativeArray<Particle> particles) => SetParticles(particles, -1, 0);
        public void SetParticles(NativeArray<Particle> particles, int size) => SetParticles(particles, size, 0);
        public void SetParticles(NativeArray<Particle> particles, int size, int offset)
            => SetFrom(i => particles[i], size < 0 ? particles.Length : Math.Min(size, particles.Length), offset);

        /// <summary>offset 0: the system's particles become the given ones; otherwise the given ones overwrite from offset on.
        /// Particles with no remaining lifetime are removed, like in Unity.</summary>
        void SetFrom(Func<int, Particle> get, int count, int offset)
        {
            var list = S.particles;
            if (offset == 0) list.Clear();
            for (int i = 0; i < count; i++)
            {
                int at = offset + i;
                if (at < list.Count) list[at] = get(i); else if (list.Count < S.maxParticles) list.Add(get(i));
            }
            list.RemoveAll(p => p.remainingLifetime <= 0);
        }

        // ------------------------------------------------------------------ simulation
        /// <summary>One frame of every particle system in the open scenes (Play mode).</summary>
        internal static void TickAll(float dt, float unscaledDt)
        {
            // snapshots: a stop action may destroy or deactivate objects while the systems are updated
            foreach (var go in System.Linq.Enumerable.ToList(MiniUnity.Play.SceneObjects()))
            {
                if (go.m_Destroyed || !go.activeInHierarchy) continue;
                foreach (var c in go.m_Components.ToArray())
                {
                    if (c is not ParticleSystem ps || ps.m_Destroyed) continue;
                    var s = ps.S;
                    if (!s.awoken) { s.awoken = true; if (s.playOnAwake && !s.playing) ps.PlayOne(); }
                    if (s.playing && !s.paused) ps.Advance(s.useUnscaledTime ? unscaledDt : dt);
                }
            }
        }

        void Advance(float dt)
        {
            var s = S;
            dt *= s.simulationSpeed;
            if (dt <= 0) return;
            float u = s.duration > 0 ? Mathf.Clamp01(s.time / s.duration) : 0;
            var gravity = Physics.gravity * s.gravityModifier.Evaluate(u, 0.5f);
            var m = M;
            bool local = s.simulationSpace == ParticleSystemSimulationSpace.Local;
            var toSim = local ? Quaternion.Inverse(transform.rotation) : transform.rotation;
            for (int i = s.particles.Count - 1; i >= 0; i--)
            {
                var p = s.particles[i];
                p.remainingLifetime -= dt;
                if (p.remainingLifetime <= 0) { s.particles.RemoveAt(i); continue; }
                p.velocity += (local ? Quaternion.Inverse(transform.rotation) * gravity : gravity) * dt;
                float t = Age(p);
                var animated = Vector3.zero; float speedMul = 1;
                if (m.velocityEnabled)
                {
                    float r = Lerp01(p, 0xA11u);
                    animated = new Vector3(m.velX.Evaluate(t, r), m.velY.Evaluate(t, r), m.velZ.Evaluate(t, r));
                    if (m.velSpace != s.simulationSpace) animated = toSim * animated;
                    speedMul = m.speedModifier.Evaluate(t, r);
                }
                p.SetAnimatedVelocity(animated);
                p.position += (p.velocity * speedMul + animated) * dt;
                p.rotation += p.angularVelocity * dt;
                if (m.rotationEnabled) p.rotation += m.rotZ.Evaluate(t, Lerp01(p, 0x707u)) * Mathf.Rad2Deg * dt;
                s.particles[i] = p;
            }
            float prev = s.time;
            s.time += dt;
            if (s.emitting && s.emissionEnabled)
            {
                float delay = s.startDelay.Evaluate(0, 0.5f);
                if (s.time > delay)
                {
                    float active = Mathf.Min(dt, s.time - delay);
                    s.emitDebt += s.rateOverTime.Evaluate(u, s.Random01()) * active;
                    int n = (int)s.emitDebt;
                    s.emitDebt -= n;
                    Emit(n);
                    // bursts fire in [prev, now) of the cycle; a loop that wraps also covers the start of the next cycle
                    float from = Mathf.Max(0, prev - delay), to = s.time - delay;
                    bool wraps = s.loop && s.duration > 0 && to >= s.duration;
                    foreach (var b in s.bursts)
                    {
                        EmitBurst(b, from, wraps ? s.duration : to);
                        if (wraps) EmitBurst(b, 0, to - s.duration);
                    }
                }
            }
            if (s.time >= s.duration && s.emitting)
            {
                if (s.loop && s.duration > 0) s.time -= s.duration;
                else s.emitting = false;
            }
            if (!s.emitting && s.particles.Count == 0 && s.playing) Stopped();
        }

        void EmitBurst(Burst b, float from, float to)
        {
            var s = S;
            int cycles = b.cycleCount <= 0 ? int.MaxValue : b.cycleCount;
            for (int k = 0; k < cycles; k++)
            {
                float at = b.time + k * Mathf.Max(0.0001f, b.repeatInterval);
                if (at >= to) break;
                if (at >= from && at < to && s.Random01() <= b.probability) Emit(Mathf.RoundToInt(b.count.Evaluate(0, s.Random01())));
            }
        }

        void Stopped()
        {
            var s = S;
            s.playing = false; s.time = 0;
            switch (s.stopAction)
            {
                case ParticleSystemStopAction.Disable: gameObject.SetActive(false); break;
                case ParticleSystemStopAction.Destroy: Destroy(gameObject); break;
                case ParticleSystemStopAction.Callback: SendMessage("OnParticleSystemStopped", SendMessageOptions.DontRequireReceiver); break;
            }
        }

        // ------------------------------------------------------------------ data types
        public partial struct MinMaxCurve
        {
            ParticleSystemCurveMode m_Mode;
            float m_CurveMultiplier, m_ConstantMin, m_ConstantMax;
            AnimationCurve m_CurveMin, m_CurveMax;

            public MinMaxCurve(float constant) { m_Mode = ParticleSystemCurveMode.Constant; m_CurveMultiplier = 0; m_CurveMin = m_CurveMax = null; m_ConstantMin = 0; m_ConstantMax = constant; }
            public MinMaxCurve(float multiplier, AnimationCurve curve) { m_Mode = ParticleSystemCurveMode.Curve; m_CurveMultiplier = multiplier; m_CurveMin = null; m_CurveMax = curve; m_ConstantMin = 0; m_ConstantMax = 0; }
            public MinMaxCurve(float multiplier, AnimationCurve min, AnimationCurve max) { m_Mode = ParticleSystemCurveMode.TwoCurves; m_CurveMultiplier = multiplier; m_CurveMin = min; m_CurveMax = max; m_ConstantMin = 0; m_ConstantMax = 0; }
            public MinMaxCurve(float min, float max) { m_Mode = ParticleSystemCurveMode.TwoConstants; m_CurveMultiplier = 0; m_CurveMin = m_CurveMax = null; m_ConstantMin = min; m_ConstantMax = max; }

            public ParticleSystemCurveMode mode { get => m_Mode; set => m_Mode = value; }
            public float curveMultiplier { get => m_CurveMultiplier; set => m_CurveMultiplier = value; }
            public float curveScalar { get => m_CurveMultiplier; set => m_CurveMultiplier = value; }
            public AnimationCurve curveMax { get => m_CurveMax; set => m_CurveMax = value; }
            public AnimationCurve curveMin { get => m_CurveMin; set => m_CurveMin = value; }
            public AnimationCurve curve { get => m_CurveMax; set => m_CurveMax = value; }
            public float constantMax { get => m_ConstantMax; set => m_ConstantMax = value; }
            public float constantMin { get => m_ConstantMin; set => m_ConstantMin = value; }
            public float constant { get => m_ConstantMax; set => m_ConstantMax = value; }

            public float Evaluate(float time) => Evaluate(time, 1f);
            public float Evaluate(float time, float lerpFactor) => m_Mode switch
            {
                ParticleSystemCurveMode.Constant => m_ConstantMax,
                ParticleSystemCurveMode.TwoConstants => Mathf.Lerp(m_ConstantMin, m_ConstantMax, lerpFactor),
                ParticleSystemCurveMode.Curve => (m_CurveMax?.Evaluate(time) ?? 0) * m_CurveMultiplier,
                _ => Mathf.Lerp(m_CurveMin?.Evaluate(time) ?? 0, m_CurveMax?.Evaluate(time) ?? 0, lerpFactor) * m_CurveMultiplier,
            };
            public static implicit operator MinMaxCurve(float constant) => new MinMaxCurve(constant);

            /// <summary>The value a "...Multiplier" property reads: the constant, or the curve multiplier.</summary>
            internal float Multiplier => m_Mode is ParticleSystemCurveMode.Constant or ParticleSystemCurveMode.TwoConstants ? m_ConstantMax : m_CurveMultiplier;
            internal MinMaxCurve WithMultiplier(float v)
            {
                var c = this;
                switch (m_Mode)
                {
                    case ParticleSystemCurveMode.Constant: c.m_ConstantMax = v; break;
                    case ParticleSystemCurveMode.TwoConstants: c.m_ConstantMin = m_ConstantMax != 0 ? m_ConstantMin * v / m_ConstantMax : v; c.m_ConstantMax = v; break;
                    default: c.m_CurveMultiplier = v; break;
                }
                return c;
            }
        }

        public partial struct MinMaxGradient
        {
            ParticleSystemGradientMode m_Mode;
            Gradient m_GradientMin, m_GradientMax;
            Color m_ColorMin, m_ColorMax;

            public MinMaxGradient(Color color) { m_Mode = ParticleSystemGradientMode.Color; m_GradientMin = m_GradientMax = null; m_ColorMin = Color.black; m_ColorMax = color; }
            public MinMaxGradient(Gradient gradient) { m_Mode = ParticleSystemGradientMode.Gradient; m_GradientMin = null; m_GradientMax = gradient; m_ColorMin = Color.black; m_ColorMax = Color.black; }
            public MinMaxGradient(Color min, Color max) { m_Mode = ParticleSystemGradientMode.TwoColors; m_GradientMin = m_GradientMax = null; m_ColorMin = min; m_ColorMax = max; }
            public MinMaxGradient(Gradient min, Gradient max) { m_Mode = ParticleSystemGradientMode.TwoGradients; m_GradientMin = min; m_GradientMax = max; m_ColorMin = Color.black; m_ColorMax = Color.black; }

            public ParticleSystemGradientMode mode { get => m_Mode; set => m_Mode = value; }
            public Gradient gradientMax { get => m_GradientMax; set => m_GradientMax = value; }
            public Gradient gradientMin { get => m_GradientMin; set => m_GradientMin = value; }
            public Color colorMax { get => m_ColorMax; set => m_ColorMax = value; }
            public Color colorMin { get => m_ColorMin; set => m_ColorMin = value; }
            public Color color { get => m_ColorMax; set => m_ColorMax = value; }
            public Gradient gradient { get => m_GradientMax; set => m_GradientMax = value; }

            public Color Evaluate(float time) => Evaluate(time, 1f);
            public Color Evaluate(float time, float lerpFactor) => m_Mode switch
            {
                ParticleSystemGradientMode.Color => m_ColorMax,
                ParticleSystemGradientMode.TwoColors => Color.Lerp(m_ColorMin, m_ColorMax, lerpFactor),
                ParticleSystemGradientMode.Gradient => m_GradientMax?.Evaluate(time) ?? Color.white,
                ParticleSystemGradientMode.RandomColor => m_GradientMax?.Evaluate(lerpFactor) ?? Color.white,
                _ => Color.Lerp(m_GradientMin?.Evaluate(time) ?? Color.white, m_GradientMax?.Evaluate(time) ?? Color.white, lerpFactor),
            };
            public static implicit operator MinMaxGradient(Color color) => new MinMaxGradient(color);
            public static implicit operator MinMaxGradient(Gradient gradient) => new MinMaxGradient(gradient);
        }

        public partial struct Burst
        {
            float m_Time, m_RepeatInterval, m_Probability;
            MinMaxCurve m_Count;
            int m_CycleCount;

            public Burst(float _time, short _count) : this(_time, new MinMaxCurve(_count)) { }
            public Burst(float _time, short _minCount, short _maxCount) : this(_time, new MinMaxCurve(_minCount, _maxCount)) { }
            public Burst(float _time, short _minCount, short _maxCount, int _cycleCount, float _repeatInterval) : this(_time, new MinMaxCurve(_minCount, _maxCount), _cycleCount, _repeatInterval) { }
            public Burst(float _time, MinMaxCurve _count) : this(_time, _count, 1, 0.01f) { }
            public Burst(float _time, MinMaxCurve _count, int _cycleCount, float _repeatInterval)
            { m_Time = _time; m_Count = _count; m_CycleCount = _cycleCount; m_RepeatInterval = _repeatInterval; m_Probability = 1; }

            public float time { get => m_Time; set => m_Time = value; }
            public MinMaxCurve count { get => m_Count; set => m_Count = value; }
            public short minCount { get => (short)m_Count.constantMin; set => m_Count.constantMin = value; }
            public short maxCount { get => (short)m_Count.constantMax; set => m_Count.constantMax = value; }
            public int cycleCount { get => m_CycleCount; set => m_CycleCount = value; }
            public float repeatInterval { get => m_RepeatInterval; set => m_RepeatInterval = value; }
            public float probability { get => m_Probability; set => m_Probability = value; }
        }

        public partial struct Particle
        {
            Vector3 m_Position, m_Velocity, m_AnimatedVelocity, m_AxisOfRotation, m_Rotation, m_AngularVelocity, m_StartSize;
            Color32 m_StartColor;
            uint m_RandomSeed;
            float m_Lifetime, m_StartLifetime;
            int m_MeshIndex;

            public Vector3 position { get => m_Position; set => m_Position = value; }
            public Vector3 velocity { get => m_Velocity; set => m_Velocity = value; }
            public Vector3 animatedVelocity => m_AnimatedVelocity;
            public Vector3 totalVelocity => m_Velocity + m_AnimatedVelocity;
            public float remainingLifetime { get => m_Lifetime; set => m_Lifetime = value; }
            public float lifetime { get => m_Lifetime; set => m_Lifetime = value; }
            public float startLifetime { get => m_StartLifetime; set => m_StartLifetime = value; }
            public Color32 startColor { get => m_StartColor; set => m_StartColor = value; }
            public Color32 color { get => m_StartColor; set => m_StartColor = value; }
            public uint randomSeed { get => m_RandomSeed; set => m_RandomSeed = value; }
            public float randomValue { get => BitConverter.Int32BitsToSingle((int)m_RandomSeed); set => m_RandomSeed = (uint)BitConverter.SingleToInt32Bits(value); }
            public Vector3 axisOfRotation { get => m_AxisOfRotation; set => m_AxisOfRotation = value; }
            public float startSize { get => m_StartSize.x; set => m_StartSize = new Vector3(value, value, value); }
            public float size { get => m_StartSize.x; set => m_StartSize = new Vector3(value, value, value); }
            public Vector3 startSize3D { get => m_StartSize; set => m_StartSize = value; }
            public float rotation { get => m_Rotation.z; set => m_Rotation = new Vector3(0, 0, value); }
            public Vector3 rotation3D { get => m_Rotation; set => m_Rotation = value; }
            public float angularVelocity { get => m_AngularVelocity.z; set => m_AngularVelocity = new Vector3(0, 0, value); }
            public Vector3 angularVelocity3D { get => m_AngularVelocity; set => m_AngularVelocity = value; }

            public float GetCurrentSize(ParticleSystem system) => system != null ? system.CurrentSize3D(this).x : m_StartSize.x;
            public Vector3 GetCurrentSize3D(ParticleSystem system) => system != null ? system.CurrentSize3D(this) : m_StartSize;
            public Color32 GetCurrentColor(ParticleSystem system) => system != null ? system.CurrentColor(this) : m_StartColor;
            internal void SetAnimatedVelocity(Vector3 v) => m_AnimatedVelocity = v;
            public void SetMeshIndex(int index) => m_MeshIndex = index;
            public int GetMeshIndex(ParticleSystem system) => m_MeshIndex;
        }

        public partial struct EmitParams
        {
            Particle m_Particle;
            bool m_PositionSet, m_VelocitySet, m_AxisOfRotationSet, m_RotationSet, m_AngularVelocitySet, m_StartSizeSet, m_StartColorSet,
                 m_RandomSeedSet, m_StartLifetimeSet, m_MeshIndexSet, m_ApplyShapeToPosition;

            public Particle particle
            {
                get => m_Particle;
                set
                {
                    m_Particle = value;
                    m_PositionSet = m_VelocitySet = m_AxisOfRotationSet = m_RotationSet = m_AngularVelocitySet = m_StartSizeSet = m_StartColorSet = m_RandomSeedSet = m_StartLifetimeSet = true;
                }
            }
            public Vector3 position { get => m_Particle.position; set { m_Particle.position = value; m_PositionSet = true; } }
            public bool applyShapeToPosition { get => m_ApplyShapeToPosition; set => m_ApplyShapeToPosition = value; }
            public Vector3 velocity { get => m_Particle.velocity; set { m_Particle.velocity = value; m_VelocitySet = true; } }
            public float startLifetime { get => m_Particle.startLifetime; set { m_Particle.startLifetime = value; m_StartLifetimeSet = true; } }
            public float startSize { get => m_Particle.startSize; set { m_Particle.startSize = value; m_StartSizeSet = true; } }
            public Vector3 startSize3D { get => m_Particle.startSize3D; set { m_Particle.startSize3D = value; m_StartSizeSet = true; } }
            public Vector3 axisOfRotation { get => m_Particle.axisOfRotation; set { m_Particle.axisOfRotation = value; m_AxisOfRotationSet = true; } }
            public float rotation { get => m_Particle.rotation; set { m_Particle.rotation = value; m_RotationSet = true; } }
            public Vector3 rotation3D { get => m_Particle.rotation3D; set { m_Particle.rotation3D = value; m_RotationSet = true; } }
            public float angularVelocity { get => m_Particle.angularVelocity; set { m_Particle.angularVelocity = value; m_AngularVelocitySet = true; } }
            public Vector3 angularVelocity3D { get => m_Particle.angularVelocity3D; set { m_Particle.angularVelocity3D = value; m_AngularVelocitySet = true; } }
            public Color32 startColor { get => m_Particle.startColor; set { m_Particle.startColor = value; m_StartColorSet = true; } }
            public uint randomSeed { get => m_Particle.randomSeed; set { m_Particle.randomSeed = value; m_RandomSeedSet = true; } }
            public int meshIndex { set { m_Particle.SetMeshIndex(value); m_MeshIndexSet = true; } }

            public void ResetPosition() => m_PositionSet = false;
            public void ResetVelocity() => m_VelocitySet = false;
            public void ResetAxisOfRotation() => m_AxisOfRotationSet = false;
            public void ResetRotation() => m_RotationSet = false;
            public void ResetAngularVelocity() => m_AngularVelocitySet = false;
            public void ResetStartSize() => m_StartSizeSet = false;
            public void ResetStartColor() => m_StartColorSet = false;
            public void ResetRandomSeed() => m_RandomSeedSet = false;
            public void ResetStartLifetime() => m_StartLifetimeSet = false;
            public void ResetMeshIndex() => m_MeshIndexSet = false;

            /// <summary>Override the defaults of a freshly made particle with the values that were set.
            /// applyShapeToPosition: the emitter's position (the shape module is not modelled) is added to the given position.</summary>
            internal void ApplyTo(ref Particle p, Vector3 emitterPosition)
            {
                // applyShapeToPosition keeps the shape module's offset around the given position
                if (m_PositionSet) p.position = m_ApplyShapeToPosition ? m_Particle.position + (p.position - emitterPosition) : m_Particle.position;
                if (m_VelocitySet) p.velocity = m_Particle.velocity;
                if (m_AxisOfRotationSet) p.axisOfRotation = m_Particle.axisOfRotation;
                if (m_RotationSet) p.rotation3D = m_Particle.rotation3D;
                if (m_AngularVelocitySet) p.angularVelocity3D = m_Particle.angularVelocity3D;
                if (m_StartSizeSet) p.startSize3D = m_Particle.startSize3D;
                if (m_StartColorSet) p.startColor = m_Particle.startColor;
                if (m_RandomSeedSet) p.randomSeed = m_Particle.randomSeed;
                if (m_StartLifetimeSet) { p.startLifetime = m_Particle.startLifetime; p.remainingLifetime = m_Particle.startLifetime; }
                if (m_MeshIndexSet) p.SetMeshIndex(m_Particle.GetMeshIndex(null));
            }
        }

        public partial struct MainModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal MainModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            State S => m_ParticleSystem.S;

            public float duration { get => S.duration; set => S.duration = Mathf.Max(0.05f, value); }
            public bool loop { get => S.loop; set => S.loop = value; }
            public bool prewarm { get => S.prewarm; set => S.prewarm = value; }
            public MinMaxCurve startDelay { get => S.startDelay; set => S.startDelay = value; }
            public float startDelayMultiplier { get => S.startDelay.Multiplier; set => S.startDelay = S.startDelay.WithMultiplier(value); }
            public MinMaxCurve startLifetime { get => S.startLifetime; set => S.startLifetime = value; }
            public float startLifetimeMultiplier { get => S.startLifetime.Multiplier; set => S.startLifetime = S.startLifetime.WithMultiplier(value); }
            public MinMaxCurve startSpeed { get => S.startSpeed; set => S.startSpeed = value; }
            public float startSpeedMultiplier { get => S.startSpeed.Multiplier; set => S.startSpeed = S.startSpeed.WithMultiplier(value); }
            public bool startSize3D { get => S.startSize3D; set => S.startSize3D = value; }
            public MinMaxCurve startSize { get => S.startSize; set => S.startSize = value; }
            public float startSizeMultiplier { get => S.startSize.Multiplier; set => S.startSize = S.startSize.WithMultiplier(value); }
            public MinMaxCurve startSizeX { get => S.startSize; set => S.startSize = value; }
            public float startSizeXMultiplier { get => S.startSize.Multiplier; set => S.startSize = S.startSize.WithMultiplier(value); }
            public MinMaxCurve startSizeY { get => S.startSizeY; set => S.startSizeY = value; }
            public float startSizeYMultiplier { get => S.startSizeY.Multiplier; set => S.startSizeY = S.startSizeY.WithMultiplier(value); }
            public MinMaxCurve startSizeZ { get => S.startSizeZ; set => S.startSizeZ = value; }
            public float startSizeZMultiplier { get => S.startSizeZ.Multiplier; set => S.startSizeZ = S.startSizeZ.WithMultiplier(value); }
            public bool startRotation3D { get => S.startRotation3D; set => S.startRotation3D = value; }
            public MinMaxCurve startRotation { get => S.startRotation; set => S.startRotation = value; }
            public float startRotationMultiplier { get => S.startRotation.Multiplier; set => S.startRotation = S.startRotation.WithMultiplier(value); }
            public MinMaxCurve startRotationX { get => S.startRotationX; set => S.startRotationX = value; }
            public float startRotationXMultiplier { get => S.startRotationX.Multiplier; set => S.startRotationX = S.startRotationX.WithMultiplier(value); }
            public MinMaxCurve startRotationY { get => S.startRotationY; set => S.startRotationY = value; }
            public float startRotationYMultiplier { get => S.startRotationY.Multiplier; set => S.startRotationY = S.startRotationY.WithMultiplier(value); }
            public MinMaxCurve startRotationZ { get => S.startRotation; set => S.startRotation = value; }
            public float startRotationZMultiplier { get => S.startRotation.Multiplier; set => S.startRotation = S.startRotation.WithMultiplier(value); }
            public float flipRotation { get => S.flipRotation; set => S.flipRotation = value; }
            public float randomizeRotationDirection { get => S.flipRotation; set => S.flipRotation = value; }
            public MinMaxGradient startColor { get => S.startColor; set => S.startColor = value; }
            public ParticleSystemGravitySource gravitySource { get => S.gravitySource; set => S.gravitySource = value; }
            public MinMaxCurve gravityModifier { get => S.gravityModifier; set => S.gravityModifier = value; }
            public float gravityModifierMultiplier { get => S.gravityModifier.Multiplier; set => S.gravityModifier = S.gravityModifier.WithMultiplier(value); }
            public ParticleSystemSimulationSpace simulationSpace { get => S.simulationSpace; set => S.simulationSpace = value; }
            public Transform customSimulationSpace { get => S.customSimulationSpace; set => S.customSimulationSpace = value; }
            public float simulationSpeed { get => S.simulationSpeed; set => S.simulationSpeed = Mathf.Max(0, value); }
            public bool useUnscaledTime { get => S.useUnscaledTime; set => S.useUnscaledTime = value; }
            public ParticleSystemScalingMode scalingMode { get => S.scalingMode; set => S.scalingMode = value; }
            public bool playOnAwake { get => S.playOnAwake; set => S.playOnAwake = value; }
            public int maxParticles { get => S.maxParticles; set => S.maxParticles = Math.Max(0, value); }
            public ParticleSystemEmitterVelocityMode emitterVelocityMode { get => S.emitterVelocityMode; set => S.emitterVelocityMode = value; }
            public Vector3 emitterVelocity { get => S.emitterVelocity; set => S.emitterVelocity = value; }
            public ParticleSystemStopAction stopAction { get => S.stopAction; set => S.stopAction = value; }
            public ParticleSystemRingBufferMode ringBufferMode { get => S.ringBufferMode; set => S.ringBufferMode = value; }
            public Vector2 ringBufferLoopRange { get => S.ringBufferLoopRange; set => S.ringBufferLoopRange = value; }
            public ParticleSystemCullingMode cullingMode { get => S.cullingMode; set => S.cullingMode = value; }
        }

        public partial struct EmissionModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal EmissionModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            State S => m_ParticleSystem.S;

            public bool enabled { get => S.emissionEnabled; set => S.emissionEnabled = value; }
            public MinMaxCurve rateOverTime { get => S.rateOverTime; set => S.rateOverTime = value; }
            public float rateOverTimeMultiplier { get => S.rateOverTime.Multiplier; set => S.rateOverTime = S.rateOverTime.WithMultiplier(value); }
            public MinMaxCurve rateOverDistance { get => S.rateOverDistance; set => S.rateOverDistance = value; }
            public float rateOverDistanceMultiplier { get => S.rateOverDistance.Multiplier; set => S.rateOverDistance = S.rateOverDistance.WithMultiplier(value); }
            public ParticleSystemEmissionType type { get => ParticleSystemEmissionType.Time; set { } }
            public MinMaxCurve rate { get => S.rateOverTime; set => S.rateOverTime = value; }
            public float rateMultiplier { get => rateOverTimeMultiplier; set => rateOverTimeMultiplier = value; }
            public int burstCount
            {
                get => S.bursts.Count;
                set { var b = S.bursts; while (b.Count > value) b.RemoveAt(b.Count - 1); while (b.Count < value) b.Add(new Burst(0, new MinMaxCurve(30))); }
            }
            public void SetBursts(Burst[] bursts) => SetBursts(bursts, bursts?.Length ?? 0);
            public void SetBursts(Burst[] bursts, int size) { S.bursts.Clear(); for (int i = 0; i < size; i++) S.bursts.Add(bursts[i]); }
            public int GetBursts(Burst[] bursts) { int n = Math.Min(bursts.Length, S.bursts.Count); for (int i = 0; i < n; i++) bursts[i] = S.bursts[i]; return n; }
            public void SetBurst(int index, Burst burst) => S.bursts[index] = burst;
            public Burst GetBurst(int index) => S.bursts[index];
        }
    }
}
