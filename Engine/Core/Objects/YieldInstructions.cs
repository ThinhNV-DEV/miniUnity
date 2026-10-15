// Custom yield instructions (UnityEngine CoreModule): the coroutine scheduler polls keepWaiting once per frame after
// Update, like Unity. WaitForSecondsRealtime uses Time.realtimeSinceStartup (unscaled play time in MiniUnity).
using System;

namespace UnityEngine
{
    public abstract partial class CustomYieldInstruction
    {
        public object Current => null;
        public bool MoveNext() => keepWaiting;
        public virtual void Reset() { }
    }

    public sealed partial class WaitUntil
    {
        readonly Func<bool> m_Predicate;
        public WaitUntil(Func<bool> predicate) { m_Predicate = predicate; }
        public override bool keepWaiting => !m_Predicate();
    }

    public sealed partial class WaitWhile
    {
        readonly Func<bool> m_Predicate;
        public WaitWhile(Func<bool> predicate) { m_Predicate = predicate; }
        public override bool keepWaiting => m_Predicate();
    }

    public partial class WaitForSecondsRealtime
    {
        float m_WaitUntilTime = -1;
        public float waitTime { get; set; }
        public WaitForSecondsRealtime(float time) { waitTime = time; }
        public override bool keepWaiting
        {
            get
            {
                if (m_WaitUntilTime < 0) m_WaitUntilTime = Time.realtimeSinceStartup + waitTime;
                bool wait = Time.realtimeSinceStartup < m_WaitUntilTime;
                if (!wait) Reset();
                return wait;
            }
        }
        public override void Reset() { m_WaitUntilTime = -1; }
    }
}
