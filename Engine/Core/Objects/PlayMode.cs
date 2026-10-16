// Play mode emulation (agent tools: `ua play`, `ua test`): a deterministic player loop over the loaded scene.
// Per frame (fixed simulated frame rate): pending Start → FixedUpdate (+ WaitForFixedUpdate) as often as the fixed step requires →
// Update → coroutines (null / WaitForSeconds / WaitUntil / nested / Coroutine) → Invoke / InvokeRepeating → LateUpdate →
// WaitForEndOfFrame. Exceptions thrown by user code are logged like Unity does (the loop goes on) and collected with stack traces.
// Physics (3D PhysX-style, 2D Box2D) and animation (Animator / Animation) run in their Unity player-loop slots; rendering is not simulated.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    public static partial class Play
    {
        public static bool IsPlaying;
        public static double Time, UnscaledTime, FixedTime;
        public static float DeltaTime = 1f / 60f, UnscaledDeltaTime = 1f / 60f, FixedDeltaTime = 0.02f, TimeScale = 1f, MaximumDeltaTime = 0.3333333f;
        public static float FrameDelta = 1f / 60f;   // simulated frame duration (real seconds)
        public static int Frame;
        public static long FixedSteps;   // fixed updates run so far (WaitForFixedUpdate in tests)
        public static bool InFixed;
        public static readonly InputScript Input = new();

        public sealed class LogEntry { public string Type, Message, Stack; public int Frame; public double Time; }
        public static readonly List<LogEntry> Log = new();
        public static readonly List<(Exception ex, string where)> Exceptions = new();

        sealed class Routine
        {
            public MonoBehaviour Owner;
            public readonly Stack<IEnumerator> Stack = new();
            public object Wait;          // current yield value
            public double Until;         // WaitForSeconds deadline (scaled time)
            public bool Done;
            public Coroutine Handle;
            public IEnumerator Root;
            public string Name;
        }
        static readonly List<Routine> routines = new();
        static readonly Dictionary<Coroutine, Routine> byHandle = new();
        sealed class Invocation { public MonoBehaviour Owner; public string Method; public double At, Repeat; public bool Repeating; }
        static readonly List<Invocation> invokes = new();
        static readonly HashSet<MonoBehaviour> started = new();
        static readonly List<(Object obj, double at)> pendingDestroy = new();
        public static void DestroyLater(Object obj, float delay) => pendingDestroy.Add((obj, Time + delay));
        static readonly Dictionary<(Type, string), MethodInfo> methods = new();

        public static void Reset()
        {
            IsPlaying = false; Time = UnscaledTime = FixedTime = 0; Frame = 0; TimeScale = 1;
            ResetRender();
            Input.Clear(); routines.Clear(); byHandle.Clear(); invokes.Clear(); started.Clear(); Log.Clear(); Exceptions.Clear(); pendingDestroy.Clear();
            Physics3D.PhysicsWorld.Reset();
            Box2D.Physics2DWorld.Reset();
            Anim.AnimationSystem.Reset();
            WebRequests.Reset();
            MouseEvents.Reset();
            Audio.AudioSystem.Reset();
            LitMotion.MotionRunner.Reset();
            UnityEngine.UIElements.UIElementsRuntimeUtility.Reset(); WorldRenderer.ClearSubmitted(); QualitySettings.ResetColorSpace(); UnityEngine.Cursor.Reset();
            UnityEngineInternal.Input.NativeInputSystem.Reset();
        }

        // ------------------------------------------------------------------ objects
        public static IEnumerable<GameObject> SceneObjects() => Hier.SceneObjects(SceneData.Active);

        /// <summary>Enabled behaviours on active objects, in hierarchy order (a new list: callers run user code over it).</summary>
        internal static List<MonoBehaviour> Active()
        {
            var list = new List<MonoBehaviour>();
            foreach (var g in SceneObjects())
            {
                if (g.m_Destroyed || !g.activeInHierarchy) continue;
                foreach (var c in g.m_Components)
                    if (c is MonoBehaviour mb && !mb.m_Destroyed && mb.m_Enabled) list.Add(mb);
            }
            return list;
        }

        internal static MethodInfo Method(Type t, string name)
        {
            if (methods.TryGetValue((t, name), out var m)) return m;
            for (var c = t; c != null && c != typeof(MonoBehaviour) && c != typeof(Behaviour) && c != typeof(Object); c = c.BaseType)
            {
                m = c.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null) break;
            }
            return methods[(t, name)] = m;
        }

        /// <summary>Call a message on a behaviour; exceptions are logged (Unity keeps running) and collected.</summary>
        public static object Call(MonoBehaviour mb, string name)
        {
            var m = Method(mb.GetType(), name);
            if (m == null) return null;
            try
            {
                var r = m.Invoke(mb, null);
                if (r is IEnumerator e && (name == "Start" || name.StartsWith("On"))) StartRoutine(mb, e, name);   // IEnumerator Start() is a coroutine
                return r;
            }
            catch (TargetInvocationException tie) { Report(tie.InnerException ?? tie, $"{mb.GetType().Name}.{name}"); }
            catch (Exception e) { Report(e, $"{mb.GetType().Name}.{name}"); }
            return null;
        }

        public static void Report(Exception e, string where)
        {
            Exceptions.Add((e, where));
            Debug.LogException(e);
        }

        // ------------------------------------------------------------------ loop
        public static void Begin()
        {
            IsPlaying = true;
            Time = UnscaledTime = FixedTime = 0; Frame = 0;
            // Awake (all), then OnEnable, in hierarchy order – MiniUnity's Lifecycle does both when an object becomes active
            foreach (var g in SceneObjects().ToList())
                foreach (var c in g.m_Components.ToList())
                    if (c is MonoBehaviour mb && !mb.m_Destroyed && g.activeInHierarchy && !mb.m_Awoken) { mb.m_Awoken = true; Call(mb, "Awake"); }
            foreach (var mb in Active()) Call(mb, "OnEnable");
            Audio.AudioSystem.CheckPlayOnAwake();
            foreach (var d in Directors()) if (d.playOnAwake && d.playableAsset != null) { d.time = d.initialTime; d.Play(); }
        }

        static IEnumerable<UnityEngine.Playables.PlayableDirector> Directors()
        {
            foreach (var g in SceneObjects().ToList())
            {
                if (g.m_Destroyed || !g.activeInHierarchy) continue;
                foreach (var c in g.m_Components)
                    if (c is UnityEngine.Playables.PlayableDirector d && !d.m_Destroyed && d.enabled) yield return d;
            }
        }

        public static void Step()
        {
            Frame++;
            WorldRenderer.ClearSubmitted();   // Graphics.Draw*/RenderMesh* of the previous frame were drawn by its cameras
            UnscaledDeltaTime = FrameDelta;
            DeltaTime = Math.Min(FrameDelta, MaximumDeltaTime) * TimeScale;
            UnscaledTime += FrameDelta;
            Time += DeltaTime;
            UnityEngine.LowLevel.PlayerLoop.RunPhase(typeof(UnityEngine.PlayerLoop.Initialization));
            InputSim.BeginFrame(Frame, DeltaTime);   // EarlyUpdate: device state and virtual axes for this frame
            UnityEngineInternal.Input.NativeInputSystem.Tick(UnityEngineInternal.Input.NativeInputUpdateType.Dynamic);   // Input System package
            UnityEngine.LowLevel.PlayerLoop.RunPhase(typeof(UnityEngine.PlayerLoop.EarlyUpdate));
            // Start for behaviours enabled since last frame
            foreach (var mb in Active())
                if (started.Add(mb)) Call(mb, "Start");
            Audio.AudioSystem.CheckPlayOnAwake();
            // fixed steps
            while (FixedTime + FixedDeltaTime * TimeScale <= Time + 1e-9 && FixedDeltaTime > 0 && TimeScale > 0)
            {
                FixedTime += FixedDeltaTime * TimeScale;
                InFixed = true; FixedSteps++;
                UnityEngineInternal.Input.NativeInputSystem.Tick(UnityEngineInternal.Input.NativeInputUpdateType.Fixed);
                UnityEngine.LowLevel.PlayerLoop.RunPhase(typeof(UnityEngine.PlayerLoop.FixedUpdate));
                foreach (var mb in Active()) Call(mb, "FixedUpdate");
                Anim.AnimationSystem.Tick(FixedDeltaTime, FixedDeltaTime, true);   // Animators in AnimatePhysics mode
                // internal physics update: ConstantForce, simulate, OnTrigger* / OnCollision* (Unity's FixedUpdate phase order)
                if (UnityEngine.Physics.simulationMode == SimulationMode.FixedUpdate) SimulatePhysics(FixedDeltaTime);
                if (UnityEngine.Physics2D.simulationMode == SimulationMode2D.FixedUpdate) SimulatePhysics2D(FixedDeltaTime);
                ResumeRoutines(r => r.Wait is WaitForFixedUpdate);
                InFixed = false;
            }
            // Rigidbody interpolation / extrapolation before Update
            if (FixedDeltaTime > 0)
            {
                float alpha = Mathf.Clamp01((float)((Time - FixedTime) / (FixedDeltaTime * Math.Max(TimeScale, 1e-6f))));
                Physics3D.PhysicsWorld.Interpolate(alpha);
                Box2D.Physics2DWorld.Interpolate(alpha);
            }
            MouseEvents.Send();   // PreUpdate.SendMouseEvents: OnMouseDown / Drag / Up / UpAsButton / Enter / Over / Exit
            UnityEngine.LowLevel.PlayerLoop.RunPhase(typeof(UnityEngine.PlayerLoop.PreUpdate));
            UnityEngine.LowLevel.PlayerLoop.RunPhase(typeof(UnityEngine.PlayerLoop.Update));
            foreach (var mb in Active()) Call(mb, "Update");
            LitMotion.MotionRunner.Tick(DeltaTime);   // LitMotion's default PlayerLoopTiming.Update
            if (UnityEngine.Physics.simulationMode == SimulationMode.Update) SimulatePhysics(DeltaTime);
            if (UnityEngine.Physics2D.simulationMode == SimulationMode2D.Update) SimulatePhysics2D(DeltaTime);
            WebRequests.Pump();   // finished UnityWebRequests raise `completed` before coroutines resume
            ResumeRoutines(r => r.Wait == null || (r.Wait is WaitForSeconds && Time >= r.Until) || (r.Wait is CustomYieldInstruction cy && !SafeKeepWaiting(cy))
                                || (r.Wait is Coroutine co && (!byHandle.TryGetValue(co, out var other) || other.Done)) || (r.Wait is AsyncOperation ao && ao.isDone));
            RunInvokes();
            foreach (var d in Directors().ToList())   // Update.DirectorUpdate: playing timelines advance and evaluate
            {
                try { d.PlayerLoopTick(DeltaTime, UnscaledDeltaTime); }
                catch (Exception e) { Report(e, "PlayableDirector"); }
            }
            PlayableRuntime.Tick(DeltaTime, UnscaledDeltaTime);   // graphs started with PlayableGraph.Play
            Anim.AnimationSystem.Tick(DeltaTime, UnscaledDeltaTime, false);   // Animator / Animation (PreLateUpdate)
            UnityEngine.ParticleSystem.TickAll(DeltaTime, UnscaledDeltaTime);   // PreLateUpdate.ParticleSystemBeginUpdateAll
            UnityEngine.LowLevel.PlayerLoop.RunPhase(typeof(UnityEngine.PlayerLoop.PreLateUpdate));
            foreach (var mb in Active()) Call(mb, "LateUpdate");
            RenderPhase();   // PostLateUpdate.FinishFrameRendering: camera callbacks and visibility (no pixels)
            ResumeRoutines(r => r.Wait is WaitForEndOfFrame);
            routines.RemoveAll(r => r.Done);
            foreach (var (o, at) in pendingDestroy.Where(p => p.at <= Time + 1e-9).ToList()) { pendingDestroy.Remove((o, at)); Object.DestroyImmediate(o); }
            Audio.AudioSystem.Update(UnscaledDeltaTime);   // PostLateUpdate.UpdateAudio: DSP clock runs on real time
            UnityEngine.LowLevel.PlayerLoop.RunPhase(typeof(UnityEngine.PlayerLoop.PostLateUpdate));
        }

        static void SimulatePhysics(float dt)
        {
            foreach (var g in SceneObjects().ToList())
            {
                if (!g.activeInHierarchy) continue;
                foreach (var c in g.m_Components)
                    if (c is ConstantForce cf && cf.enabled && g.GetComponent<Rigidbody>() is Rigidbody rb)
                    {
                        rb.AddForce(cf.force); rb.AddRelativeForce(cf.relativeForce);
                        rb.AddTorque(cf.torque); rb.AddRelativeTorque(cf.relativeTorque);
                    }
            }
            try { Physics3D.PhysicsWorld.Simulate(dt); }
            catch (Exception e) { Report(e, "Physics.Simulate"); }
            PhysicsMessages.Dispatch();
        }

        static void SimulatePhysics2D(float dt)
        {
            try { Box2D.Physics2DWorld.Simulate(dt); }
            catch (Exception e) { Report(e, "Physics2D.Simulate"); }
            PhysicsMessages2D.Dispatch();
        }

        static bool SafeKeepWaiting(CustomYieldInstruction cy)
        {
            try { return cy.keepWaiting; } catch (Exception e) { Report(e, cy.GetType().Name + ".keepWaiting"); return false; }
        }

        // ------------------------------------------------------------------ coroutines
        public static Coroutine StartRoutine(MonoBehaviour owner, IEnumerator e, string name = null)
        {
            if (e == null) return null;
            var r = new Routine { Owner = owner, Root = e, Name = name ?? e.GetType().Name, Handle = new Coroutine() };
            r.Stack.Push(e);
            routines.Add(r); byHandle[r.Handle] = r;
            Advance(r);   // runs synchronously up to the first yield, like Unity
            return r.Handle;
        }

        public static void StopRoutine(MonoBehaviour owner, Func<IEnumerator, Coroutine, string, bool> match)
        {
            foreach (var r in routines.Where(r => r.Owner == owner && match(r.Root, r.Handle, r.Name))) r.Done = true;
        }

        static void ResumeRoutines(Func<Routine, bool> ready)
        {
            foreach (var r in routines.ToList())
            {
                if (r.Done) continue;
                if (r.Owner == null || r.Owner.m_Destroyed || r.Owner.m_GameObject == null || !r.Owner.m_GameObject.activeInHierarchy) { r.Done = true; continue; }
                if (ready(r)) Advance(r);
            }
        }

        static void Advance(Routine r)
        {
            try
            {
                while (r.Stack.Count > 0)
                {
                    var top = r.Stack.Peek();
                    if (!top.MoveNext()) { r.Stack.Pop(); if (r.Stack.Count == 0) { r.Done = true; return; } continue; }
                    var y = top.Current;
                    if (y is IEnumerator nested && y is not CustomYieldInstruction) { r.Stack.Push(nested); continue; }
                    r.Wait = y;
                    if (y is WaitForSeconds ws) r.Until = Time + ws.m_Seconds;
                    return;
                }
                r.Done = true;
            }
            catch (Exception e) { r.Done = true; Report(e, "coroutine " + r.Name); }
        }

        // ------------------------------------------------------------------ Invoke
        public static void AddInvoke(MonoBehaviour mb, string method, float delay, float repeat, bool repeating) =>
            invokes.Add(new Invocation { Owner = mb, Method = method, At = Time + delay, Repeat = repeat, Repeating = repeating });
        public static void CancelInvoke(MonoBehaviour mb, string method) => invokes.RemoveAll(i => i.Owner == mb && (method == null || i.Method == method));
        public static bool IsInvoking(MonoBehaviour mb, string method) => invokes.Any(i => i.Owner == mb && (method == null || i.Method == method));

        static void RunInvokes()
        {
            foreach (var i in invokes.ToList())
            {
                if (i.Owner == null || i.Owner.m_Destroyed) { invokes.Remove(i); continue; }
                if (Time + 1e-9 < i.At) continue;
                if (i.Repeating && i.Repeat > 0) i.At += i.Repeat; else invokes.Remove(i);
                Call(i.Owner, i.Method);
            }
        }

        // ------------------------------------------------------------------ logging
        public static Application.LogCallback LogHandlers;
        /// <summary>Keep logs outside Play mode too (editor commands: menu, window).</summary>
        public static bool CaptureLogs;

        public static void OnLog(string level, string message, string stack)
        {
            if (!IsPlaying && !CaptureLogs && LogHandlers == null) return;
            if (IsPlaying || CaptureLogs) Log.Add(new LogEntry { Type = level, Message = message, Stack = stack, Frame = Frame, Time = Time });
            var lt = level switch { "Warning" => LogType.Warning, "Error" => LogType.Error, "Exception" => LogType.Exception, "Assert" => LogType.Assert, _ => LogType.Log };
            try { LogHandlers?.Invoke(message, stack, lt); } catch { }
        }
    }
}

namespace UnityEngine
{
    public sealed partial class WaitForSeconds
    {
        public WaitForSeconds(float seconds) { m_Seconds = seconds; }
        internal WaitForSeconds() { }
    }
}
