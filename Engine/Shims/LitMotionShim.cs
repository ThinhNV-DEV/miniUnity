// Minimal LitMotion surface: Ease + EaseUtility come from the real sources. The motion runtime is a small scheduler
// (MotionRunner) ticked by MiniUnity's player loop in the Update phase (LitMotion's default timing): Create → WithEase /
// WithDelay / WithLoops → Bind (applied immediately, like LitMotion's immediate bind), MotionHandle.Time / Cancel / IsActive,
// CompositeMotionHandle. Loop types: Restart, Yoyo (Flip behaves like Yoyo), Incremental.
using System;
namespace Unity.Burst { [AttributeUsage(AttributeTargets.All)] public class BurstCompileAttribute : Attribute { } }
namespace Unity.Mathematics
{
    public static class math
    {
        public const float PI = (float)Math.PI;
        public static float sin(float x) => MathF.Sin(x);
        public static float cos(float x) => MathF.Cos(x);
        public static float pow(float x, float y) => MathF.Pow(x, y);
        public static float sqrt(float x) => MathF.Sqrt(x);
        public static float abs(float x) => MathF.Abs(x);
        public static float clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
        public static float lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
namespace LitMotion
{
    public enum LoopType { Restart, Flip, Incremental, Yoyo }

    public struct MotionHandle
    {
        internal int Id;
        public bool IsActive() => MotionRunner.Find(Id) != null;
        public double Time
        {
            get => MotionRunner.Find(Id)?.Elapsed ?? 0;
            set { var m = MotionRunner.Find(Id); if (m != null) { m.Elapsed = value; m.Apply(); } }
        }
        public void Cancel() => MotionRunner.Remove(Id);
        public void Complete() { var m = MotionRunner.Find(Id); if (m != null) { m.Elapsed = m.Delay + m.Duration * Math.Max(1, m.Loops); m.Apply(); MotionRunner.Remove(Id); } }
        public MotionHandle AddTo(CompositeMotionHandle c) { c?.Add(this); return this; }
    }

    public class CompositeMotionHandle
    {
        readonly System.Collections.Generic.List<MotionHandle> m_Handles = new();
        public void Cancel() { foreach (var h in m_Handles) h.Cancel(); m_Handles.Clear(); }
        public void Add(MotionHandle h) => m_Handles.Add(h);
    }

    internal sealed class Motion
    {
        public int Id; public double Elapsed; public float Duration, Delay; public int Loops = 1; public LoopType Loop; public Ease Ease;
        public Action<float> Apply01;   // eased progress (with the loop count folded in for Incremental)
        public bool Done;
        public void Apply()
        {
            double local = Elapsed - Delay;
            if (local < 0) { Apply01(0); return; }
            float d = Math.Max(1e-6f, Duration);
            int cycle = (int)Math.Floor(local / d);
            float t = (float)((local - cycle * d) / d);
            if (Loops >= 0 && cycle >= Loops) { cycle = Math.Max(0, Loops - 1); t = 1; Done = true; }
            bool back = (Loop == LoopType.Yoyo || Loop == LoopType.Flip) && cycle % 2 == 1;
            float e = EaseUtility.Evaluate(back ? 1 - t : t, Ease);
            Apply01(Loop == LoopType.Incremental ? e + cycle : e);
        }
    }

    public static class MotionRunner
    {
        static readonly System.Collections.Generic.Dictionary<int, Motion> s_Motions = new();
        static int s_Next = 1;
        internal static Motion Find(int id) => id != 0 && s_Motions.TryGetValue(id, out var m) && !m.Done ? m : null;
        internal static void Remove(int id) => s_Motions.Remove(id);
        internal static MotionHandle Add(Motion m)
        {
            m.Id = s_Next++;
            s_Motions[m.Id] = m;
            try { m.Apply(); } catch (Exception e) { UnityEngine.Debug.LogException(e); s_Motions.Remove(m.Id); }
            return new MotionHandle { Id = m.Id };
        }
        /// <summary>Advance every motion by the frame's delta time (called from MiniUnity's player loop).</summary>
        public static void Tick(float dt)
        {
            if (s_Motions.Count == 0) return;
            foreach (var m in new System.Collections.Generic.List<Motion>(s_Motions.Values))
            {
                m.Elapsed += dt;
                try { m.Apply(); } catch (Exception e) { UnityEngine.Debug.LogException(e); m.Done = true; }
                if (m.Done) s_Motions.Remove(m.Id);
            }
        }
        public static void Reset() { s_Motions.Clear(); }
    }

    public struct MotionBuilder<T>
    {
        internal T From, To; internal float Duration, Delay; internal int Loops; internal LoopType Loop; internal Ease Ease; internal Func<T, T, float, T> Lerp;
        internal bool LoopsSet;
        public MotionBuilder<T> WithEase(Ease e) { Ease = e; return this; }
        public MotionBuilder<T> WithDelay(float d) { Delay = d; return this; }
        public MotionBuilder<T> WithLoops(int n, LoopType t = LoopType.Restart) { Loops = n; Loop = t; LoopsSet = true; return this; }
        public MotionHandle Bind<S>(S state, Action<T, S> a) where S : class { var s = state; return Bind(v => a(v, s)); }
        public MotionHandle Bind(Action<T> a)
        {
            var self = this;
            var m = new Motion { Duration = Duration, Delay = Delay, Loops = LoopsSet ? Loops : 1, Loop = Loop, Ease = Ease };
            m.Apply01 = e => a(self.Lerp(self.From, self.To, e));
            return MotionRunner.Add(m);
        }
    }

    public static class LMotion
    {
        public static MotionBuilder<float> Create(float a, float b, float d) => new() { From = a, To = b, Duration = d, Lerp = (x, y, t) => x + (y - x) * t };
        public static MotionBuilder<UnityEngine.Vector2> Create(UnityEngine.Vector2 a, UnityEngine.Vector2 b, float d) => new() { From = a, To = b, Duration = d, Lerp = (x, y, t) => x + (y - x) * t };
        public static MotionBuilder<UnityEngine.Vector3> Create(UnityEngine.Vector3 a, UnityEngine.Vector3 b, float d) => new() { From = a, To = b, Duration = d, Lerp = (x, y, t) => x + (y - x) * t };
    }
}
