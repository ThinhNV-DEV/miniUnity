using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public partial class AnimationCurve
    {
        internal List<Keyframe> m_Keys = new();
        internal WrapMode m_PreInfinity = WrapMode.ClampForever, m_PostInfinity = WrapMode.ClampForever;
        public AnimationCurve() { }
        public AnimationCurve(params Keyframe[] keys) { m_Keys = keys?.OrderBy(k => k.time).ToList() ?? new(); }
        public Keyframe[] keys { get => m_Keys.ToArray(); set => m_Keys = value?.OrderBy(k => k.time).ToList() ?? new(); }
        public Keyframe this[int index] => m_Keys[index];
        public int length => m_Keys.Count;
        public WrapMode preWrapMode { get => m_PreInfinity; set => m_PreInfinity = value; }
        public WrapMode postWrapMode { get => m_PostInfinity; set => m_PostInfinity = value; }
        public int AddKey(float time, float value) => AddKey(new Keyframe(time, value));
        public int AddKey(Keyframe key)
        {
            for (int i = 0; i < m_Keys.Count; i++) if (Mathf.Approximately(m_Keys[i].time, key.time)) return -1;
            int idx = m_Keys.FindIndex(k => k.time > key.time); if (idx < 0) idx = m_Keys.Count;
            m_Keys.Insert(idx, key); return idx;
        }
        public int MoveKey(int index, Keyframe key) { m_Keys.RemoveAt(index); return AddKey(key); }
        public void RemoveKey(int index) => m_Keys.RemoveAt(index);
        public void ClearKeys() => m_Keys.Clear();
        public void SmoothTangents(int index, float weight) { }
        public void CopyFrom(AnimationCurve other) { m_Keys = new(other.m_Keys); m_PreInfinity = other.m_PreInfinity; m_PostInfinity = other.m_PostInfinity; }
        public float Evaluate(float time)
        {
            if (m_Keys.Count == 0) return 0;
            if (m_Keys.Count == 1) return m_Keys[0].value;
            float t0 = m_Keys[0].time, t1 = m_Keys[^1].time, span = t1 - t0;
            // pre / post wrap modes (Loop, PingPong; anything else clamps)
            if (span > 0 && (time < t0 && m_PreInfinity is WrapMode.Loop or WrapMode.PingPong || time > t1 && m_PostInfinity is WrapMode.Loop or WrapMode.PingPong))
            {
                var mode = time < t0 ? m_PreInfinity : m_PostInfinity;
                float rel = time - t0;
                if (mode == WrapMode.Loop) time = t0 + (rel - MathF.Floor(rel / span) * span);
                else { float m = rel - MathF.Floor(rel / (2 * span)) * 2 * span; time = t0 + (m > span ? 2 * span - m : m); }
            }
            if (time <= t0) return m_Keys[0].value;
            if (time >= t1) return m_Keys[^1].value;
            int i = 0; while (i < m_Keys.Count - 2 && time > m_Keys[i + 1].time) i++;
            var a = m_Keys[i]; var b = m_Keys[i + 1];
            float dt = b.time - a.time; if (dt <= 0) return b.value;
            float t = (time - a.time) / dt;
            float m0 = a.outTangent * dt, m1 = b.inTangent * dt;
            if (float.IsInfinity(m0) || float.IsInfinity(m1)) return a.value;
            float t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * a.value + (t3 - 2 * t2 + t) * m0 + (t3 - t2) * m1 + (-2 * t3 + 3 * t2) * b.value;
        }
        public static AnimationCurve Constant(float timeStart, float timeEnd, float value) => Linear(timeStart, value, timeEnd, value);
        public static AnimationCurve Linear(float timeStart, float valueStart, float timeEnd, float valueEnd)
        {
            if (timeStart == timeEnd) return new AnimationCurve(new Keyframe(timeStart, valueStart));
            float s = (valueEnd - valueStart) / (timeEnd - timeStart);
            return new AnimationCurve(new Keyframe(timeStart, valueStart, 0, s), new Keyframe(timeEnd, valueEnd, s, 0));
        }
        public static AnimationCurve EaseInOut(float timeStart, float valueStart, float timeEnd, float valueEnd)
        {
            if (timeStart == timeEnd) return new AnimationCurve(new Keyframe(timeStart, valueStart));
            return new AnimationCurve(new Keyframe(timeStart, valueStart, 0, 0), new Keyframe(timeEnd, valueEnd, 0, 0));
        }
        public override bool Equals(object o) => o is AnimationCurve c && Equals(c);
        public bool Equals(AnimationCurve other) => other != null && other.m_Keys.SequenceEqual(m_Keys) && other.m_PreInfinity == m_PreInfinity && other.m_PostInfinity == m_PostInfinity;
        public override int GetHashCode() => m_Keys.Count;
    }

    public partial class RectOffset
    {
        internal int m_LeftV, m_RightV, m_TopV, m_BottomV;
        public RectOffset() { }
        public RectOffset(int left, int right, int top, int bottom) { m_LeftV = left; m_RightV = right; m_TopV = top; m_BottomV = bottom; }
        public int left { get => m_LeftV; set => m_LeftV = value; }
        public int right { get => m_RightV; set => m_RightV = value; }
        public int top { get => m_TopV; set => m_TopV = value; }
        public int bottom { get => m_BottomV; set => m_BottomV = value; }
        public int horizontal => m_LeftV + m_RightV;
        public int vertical => m_TopV + m_BottomV;
        public Rect Add(Rect r) => new Rect(r.x - left, r.y - top, r.width + horizontal, r.height + vertical);
        public Rect Remove(Rect r) => new Rect(r.x + left, r.y + top, r.width - horizontal, r.height - vertical);
        public override string ToString() => $"RectOffset (l:{left} r:{right} t:{top} b:{bottom})";
        public string ToString(string format) => ToString();
        public string ToString(string format, IFormatProvider p) => ToString();
    }

    public sealed partial class RequireComponent
    {
        public RequireComponent(Type requiredComponent) { m_Type0 = requiredComponent; }
        public RequireComponent(Type requiredComponent, Type requiredComponent2) { m_Type0 = requiredComponent; m_Type1 = requiredComponent2; }
        public RequireComponent(Type requiredComponent, Type requiredComponent2, Type requiredComponent3) { m_Type0 = requiredComponent; m_Type1 = requiredComponent2; m_Type2 = requiredComponent3; }
    }
}
