// Playables API surface (handles, Playable/ScriptPlayable, outputs, extensions) over the managed PlayableRuntime.
// Every type here is listed as "Type: *" in config/stubs.manual.cfg: all its members come from this file.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using Object = UnityEngine.Object;
using RT = MiniUnity.PlayableRuntime;

namespace UnityEngine.Playables
{
    public partial struct PlayableHandle
    {
        internal IntPtr m_Handle;
        internal uint m_Version;
        internal T GetObject<T>() where T : class, IPlayableBehaviour => RT.N(this)?.behaviour as T;
        internal bool IsPlayableOfType<T>() => RT.N(this)?.type == typeof(T);
        internal Playable GetInput(int inputPort) { var n = RT.N(this); return n != null && inputPort >= 0 && inputPort < n.inputs.Count ? new Playable(n.inputs[inputPort].src) : Playable.Null; }
        internal Playable GetOutput(int outputPort) { var n = RT.N(this); return n != null && outputPort >= 0 && outputPort < n.outputs.Count ? new Playable(n.outputs[outputPort]) : Playable.Null; }
        internal bool SetInputWeight(int inputIndex, float weight)
        {
            var n = RT.N(this);
            if (n == null || inputIndex < 0 || inputIndex >= n.inputs.Count) return false;
            n.inputs[inputIndex] = (n.inputs[inputIndex].src, weight); return true;
        }
        internal float GetInputWeight(int inputIndex) { var n = RT.N(this); return n != null && inputIndex >= 0 && inputIndex < n.inputs.Count ? n.inputs[inputIndex].weight : 0; }
        internal void Destroy() => RT.DestroyNode(this);
        public static bool operator ==(PlayableHandle x, PlayableHandle y) => x.m_Handle == y.m_Handle && x.m_Version == y.m_Version;
        public static bool operator !=(PlayableHandle x, PlayableHandle y) => !(x == y);
        public override bool Equals(object p) => p is PlayableHandle h && this == h;
        public bool Equals(PlayableHandle other) => this == other;
        public override int GetHashCode() => m_Handle.GetHashCode() ^ m_Version.GetHashCode();
        internal static bool CompareVersion(PlayableHandle lhs, PlayableHandle rhs) => lhs == rhs;
        internal bool CheckInputBounds(int inputIndex) => CheckInputBounds(inputIndex, false);
        internal bool CheckInputBounds(int inputIndex, bool acceptAny)
        {
            if (inputIndex == -1 && acceptAny) return true;
            if (inputIndex < 0) throw new IndexOutOfRangeException("Index must be greater than 0");
            var n = RT.N(this);
            if (n == null || n.inputs.Count <= inputIndex) throw new IndexOutOfRangeException($"inputIndex {inputIndex} is greater than the number of available inputs ({n?.inputs.Count ?? 0}).");
            return true;
        }
        internal bool IsNull() => m_Handle == IntPtr.Zero;
        internal bool IsValid() => RT.N(this) != null;
        internal Type GetPlayableType() => RT.N(this)?.type;
        internal Type GetJobType() => null;
        internal void SetScriptInstance(object scriptInstance) { if (RT.N(this) is { } n) n.behaviour = scriptInstance; }
        internal bool CanChangeInputs() => true;
        internal bool CanSetWeights() => true;
        internal bool CanDestroy() => true;
        internal PlayState GetPlayState() => RT.N(this)?.state ?? PlayState.Paused;
        internal void Play() { if (RT.N(this) is { } n) n.state = PlayState.Playing; }
        internal void Pause() { if (RT.N(this) is { } n) n.state = PlayState.Paused; }
        internal double GetSpeed() => RT.N(this)?.speed ?? 0;
        internal void SetSpeed(double value) { if (RT.N(this) is { } n) n.speed = value; }
        internal double GetTime() => RT.N(this)?.time ?? 0;
        internal void SetTime(double value) => RT.SetTime(this, value);
        internal bool IsDone() => RT.N(this)?.done ?? false;
        internal void SetDone(bool value) { if (RT.N(this) is { } n) n.done = value; }
        internal double GetDuration() => RT.N(this)?.duration ?? 0;
        internal void SetDuration(double value) { if (RT.N(this) is { } n) n.duration = value; }
        internal bool GetPropagateSetTime() => RT.N(this)?.propagate ?? false;
        internal void SetPropagateSetTime(bool value) { if (RT.N(this) is { } n) n.propagate = value; }
        internal PlayableGraph GetGraph() => RT.N(this) is { } n ? RT.GraphOf(n.graph) : default;
        internal int GetInputCount() => RT.N(this)?.inputs.Count ?? 0;
        internal void SetInputCount(int value)
        {
            var n = RT.N(this);
            if (n == null) return;
            while (n.inputs.Count > value) { RT.Disconnect(this, n.inputs.Count - 1); n.inputs.RemoveAt(n.inputs.Count - 1); }
            while (n.inputs.Count < value) n.inputs.Add((Null, 0));
        }
        internal int GetOutputCount() => RT.N(this)?.outputs.Count ?? 0;
        internal void SetOutputCount(int value) { }
        internal void SetInputWeight(PlayableHandle input, float weight)
        {
            var n = RT.N(this);
            if (n == null) return;
            for (int i = 0; i < n.inputs.Count; i++) if (n.inputs[i].src == input) { n.inputs[i] = (input, weight); return; }
        }
        internal void SetDelay(double delay) { if (RT.N(this) is { } n) n.delay = delay; }
        internal double GetDelay() => RT.N(this)?.delay ?? 0;
        internal bool IsDelayed() => (RT.N(this)?.delay ?? 0) > 0;
        internal double GetPreviousTime() => RT.N(this)?.prevTime ?? 0;
        internal void SetLeadTime(float value) { if (RT.N(this) is { } n) n.leadTime = value; }
        internal float GetLeadTime() => RT.N(this)?.leadTime ?? 0;
        internal PlayableTraversalMode GetTraversalMode() => RT.N(this)?.traversal ?? PlayableTraversalMode.Mix;
        internal void SetTraversalMode(PlayableTraversalMode mode) { if (RT.N(this) is { } n) n.traversal = mode; }
        internal IntPtr GetJobData() => IntPtr.Zero;
        internal DirectorWrapMode GetTimeWrapMode() => RT.N(this)?.wrap ?? DirectorWrapMode.Loop;
        internal void SetTimeWrapMode(DirectorWrapMode mode) { if (RT.N(this) is { } n) n.wrap = mode; }
        public static PlayableHandle Null => default;
    }

    public partial struct Playable
    {
        PlayableHandle m_Handle;
        public static Playable Create(PlayableGraph graph, int inputCount = 0) => new Playable(RT.CreateNode(graph, typeof(Playable), inputCount));
        internal Playable(PlayableHandle handle) { m_Handle = handle; }
        public PlayableHandle GetHandle() => m_Handle;
        public bool IsPlayableOfType<T>() where T : struct, IPlayable => m_Handle.IsPlayableOfType<T>();
        public Type GetPlayableType() => m_Handle.GetPlayableType();
        public bool Equals(Playable other) => m_Handle == other.m_Handle;
        public override bool Equals(object o) => o is Playable p && Equals(p);
        public override int GetHashCode() => m_Handle.GetHashCode();
        public static Playable Null => default;
    }

    public partial struct ScriptPlayable<T>
    {
        PlayableHandle m_Handle;
        public static ScriptPlayable<T> Create(PlayableGraph graph, int inputCount = 0) => Create(graph, null, inputCount);
        public static ScriptPlayable<T> Create(PlayableGraph graph, T template, int inputCount = 0)
        {
            var h = RT.CreateNode(graph, typeof(ScriptPlayable<T>), inputCount);
            if (!h.IsValid()) return Null;
            T b = template == null ? new T() : template is ICloneable c ? (T)c.Clone() : template;
            h.SetScriptInstance(b);
            var p = new ScriptPlayable<T>(h);
            b.OnPlayableCreate(p);
            return p;
        }
        internal ScriptPlayable(PlayableHandle handle)
        {
            if (handle.IsValid() && !typeof(T).IsAssignableFrom(handle.GetPlayableType()) && !(handle.GetObject<IPlayableBehaviour>() is T))
                throw new InvalidCastException($"Incompatible handle: Trying to assign a playable data of type `{handle.GetPlayableType()}` that is not compatible with the PlayableBehaviour of type `{typeof(T)}`.");
            m_Handle = handle;
        }
        public PlayableHandle GetHandle() => m_Handle;
        public T GetBehaviour() => m_Handle.GetObject<T>();
        public static implicit operator Playable(ScriptPlayable<T> playable) => new Playable(playable.m_Handle);
        public static explicit operator ScriptPlayable<T>(Playable playable) => new ScriptPlayable<T>(playable.GetHandle());
        public bool Equals(ScriptPlayable<T> other) => m_Handle == other.m_Handle;
        public static ScriptPlayable<T> Null => default;
    }

    public partial struct FrameData
    {
        internal ulong m_FrameID;
        internal double m_DeltaTime;
        internal float m_Weight;
        internal float m_EffectiveWeight;
        internal double m_EffectiveParentDelay;
        internal float m_EffectiveParentSpeed;
        internal float m_EffectiveSpeed;
        internal Flags m_Flags;
        internal PlayableOutput m_Output;
        public ulong frameId => m_FrameID;
        public float deltaTime => (float)m_DeltaTime;
        public float weight => m_Weight;
        public float effectiveWeight => m_EffectiveWeight;
        [Obsolete] public double effectiveParentDelay => m_EffectiveParentDelay;
        public float effectiveParentSpeed => m_EffectiveParentSpeed;
        public float effectiveSpeed => m_EffectiveSpeed;
        public EvaluationType evaluationType => (m_Flags & Flags.Evaluate) != 0 ? EvaluationType.Evaluate : EvaluationType.Playback;
        public bool seekOccurred => (m_Flags & Flags.SeekOccured) != 0;
        public bool timeLooped => (m_Flags & Flags.Loop) != 0;
        public bool timeHeld => (m_Flags & Flags.Hold) != 0;
        public PlayableOutput output => m_Output;
        public PlayState effectivePlayState => (m_Flags & Flags.EffectivePlayStatePlaying) != 0 ? PlayState.Playing : (m_Flags & Flags.EffectivePlayStateDelayed) != 0 ? PlayState.Delayed : PlayState.Paused;
    }

    public static partial class PlayableExtensions
    {
        public static bool IsNull<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().IsNull();
        public static bool IsValid<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().IsValid();
        public static void Destroy<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().Destroy();
        public static PlayableGraph GetGraph<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetGraph();
        [Obsolete] public static void SetPlayState<U>(this U playable, PlayState value) where U : struct, IPlayable { if (value == PlayState.Playing) playable.GetHandle().Play(); else playable.GetHandle().Pause(); }
        public static PlayState GetPlayState<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetPlayState();
        public static void Play<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().Play();
        public static void Pause<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().Pause();
        public static void SetSpeed<U>(this U playable, double value) where U : struct, IPlayable => playable.GetHandle().SetSpeed(value);
        public static double GetSpeed<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetSpeed();
        public static void SetDuration<U>(this U playable, double value) where U : struct, IPlayable => playable.GetHandle().SetDuration(value);
        public static double GetDuration<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetDuration();
        public static void SetTime<U>(this U playable, double value) where U : struct, IPlayable => playable.GetHandle().SetTime(value);
        public static double GetTime<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetTime();
        public static double GetPreviousTime<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetPreviousTime();
        public static void SetDone<U>(this U playable, bool value) where U : struct, IPlayable => playable.GetHandle().SetDone(value);
        public static bool IsDone<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().IsDone();
        public static void SetPropagateSetTime<U>(this U playable, bool value) where U : struct, IPlayable => playable.GetHandle().SetPropagateSetTime(value);
        public static bool GetPropagateSetTime<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetPropagateSetTime();
        public static bool CanChangeInputs<U>(this U playable) where U : struct, IPlayable => true;
        public static bool CanSetWeights<U>(this U playable) where U : struct, IPlayable => true;
        public static bool CanDestroy<U>(this U playable) where U : struct, IPlayable => true;
        public static void SetInputCount<U>(this U playable, int value) where U : struct, IPlayable => playable.GetHandle().SetInputCount(value);
        public static int GetInputCount<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetInputCount();
        public static void SetOutputCount<U>(this U playable, int value) where U : struct, IPlayable => playable.GetHandle().SetOutputCount(value);
        public static int GetOutputCount<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetOutputCount();
        public static Playable GetInput<U>(this U playable, int inputPort) where U : struct, IPlayable => playable.GetHandle().GetInput(inputPort);
        public static Playable GetOutput<U>(this U playable, int outputPort) where U : struct, IPlayable => playable.GetHandle().GetOutput(outputPort);
        public static void SetInputWeight<U>(this U playable, int inputIndex, float weight) where U : struct, IPlayable => playable.GetHandle().SetInputWeight(inputIndex, weight);
        public static void SetInputWeight<U, V>(this U playable, V input, float weight) where U : struct, IPlayable where V : struct, IPlayable => playable.GetHandle().SetInputWeight(input.GetHandle(), weight);
        public static float GetInputWeight<U>(this U playable, int inputIndex) where U : struct, IPlayable => playable.GetHandle().GetInputWeight(inputIndex);
        public static void ConnectInput<U, V>(this U playable, int inputIndex, V sourcePlayable, int sourceOutputIndex) where U : struct, IPlayable where V : struct, IPlayable => ConnectInput(playable, inputIndex, sourcePlayable, sourceOutputIndex, 0f);
        public static void ConnectInput<U, V>(this U playable, int inputIndex, V sourcePlayable, int sourceOutputIndex, float weight) where U : struct, IPlayable where V : struct, IPlayable
        {
            RT.Connect(sourcePlayable.GetHandle(), sourceOutputIndex, playable.GetHandle(), inputIndex);
            playable.GetHandle().SetInputWeight(inputIndex, weight);
        }
        public static void DisconnectInput<U>(this U playable, int inputPort) where U : struct, IPlayable => RT.Disconnect(playable.GetHandle(), inputPort);
        public static int AddInput<U, V>(this U playable, V sourcePlayable, int sourceOutputIndex, float weight = 0f) where U : struct, IPlayable where V : struct, IPlayable
        {
            int idx = playable.GetInputCount();
            playable.SetInputCount(idx + 1);
            ConnectInput(playable, idx, sourcePlayable, sourceOutputIndex, weight);
            return idx;
        }
        [Obsolete] public static void SetDelay<U>(this U playable, double delay) where U : struct, IPlayable => playable.GetHandle().SetDelay(delay);
        [Obsolete] public static double GetDelay<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetDelay();
        [Obsolete] public static bool IsDelayed<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().IsDelayed();
        public static void SetLeadTime<U>(this U playable, float value) where U : struct, IPlayable => playable.GetHandle().SetLeadTime(value);
        public static float GetLeadTime<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetLeadTime();
        public static PlayableTraversalMode GetTraversalMode<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetTraversalMode();
        public static void SetTraversalMode<U>(this U playable, PlayableTraversalMode mode) where U : struct, IPlayable => playable.GetHandle().SetTraversalMode(mode);
        internal static DirectorWrapMode GetTimeWrapMode<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetTimeWrapMode();
        internal static void SetTimeWrapMode<U>(this U playable, DirectorWrapMode value) where U : struct, IPlayable => playable.GetHandle().SetTimeWrapMode(value);
    }

    public partial struct PlayableGraph
    {
        internal IntPtr m_Handle;
        internal uint m_Version;
        public Playable GetRootPlayable(int index) => new Playable(GetRootPlayableInternal(index));
        public bool Connect<U, V>(U source, int sourceOutputPort, V destination, int destinationInputPort) where U : struct, IPlayable where V : struct, IPlayable
            => RT.Connect(source.GetHandle(), sourceOutputPort, destination.GetHandle(), destinationInputPort);
        public void Disconnect<U>(U input, int inputPort) where U : struct, IPlayable => RT.Disconnect(input.GetHandle(), inputPort);
        public void DestroyPlayable<U>(U playable) where U : struct, IPlayable => RT.DestroyNode(playable.GetHandle());
        public void DestroySubgraph<U>(U playable) where U : struct, IPlayable
        {
            var h = playable.GetHandle();
            for (int i = 0; i < h.GetInputCount(); i++) DestroySubgraph(h.GetInput(i));
            RT.DestroyNode(h);
        }
        public void DestroyOutput<U>(U output) where U : struct, IPlayableOutput => DestroyOutputInternal(output.GetHandle());
        public int GetOutputCountByType<T>() where T : struct, IPlayableOutput => Outputs().Count(o => RT.O(o)?.type == typeof(T));
        public PlayableOutput GetOutput(int index) { var l = Outputs().ToList(); return index >= 0 && index < l.Count ? new PlayableOutput(l[index]) : PlayableOutput.Null; }
        public PlayableOutput GetOutputByType<T>(int index) where T : struct, IPlayableOutput { var l = Outputs().Where(o => RT.O(o)?.type == typeof(T)).ToList(); return index >= 0 && index < l.Count ? new PlayableOutput(l[index]) : PlayableOutput.Null; }
        IEnumerable<PlayableOutputHandle> Outputs() => RT.G(this) is { } g ? g.outputs.Select(p => RT.OutputHandleOf((int)(long)p)).Where(h => RT.O(h) != null) : Enumerable.Empty<PlayableOutputHandle>();
        public void Evaluate() => Evaluate(0f);
        public static PlayableGraph Create() => Create(null);
        public static PlayableGraph Create(string name) => RT.CreateGraph(name);
        public void Destroy() => RT.DestroyGraph(this);
        public bool IsValid() => RT.G(this) != null;
        public bool IsPlaying() => RT.G(this)?.playing ?? false;
        public bool IsDone() => false;
        public void Play() { if (RT.G(this) is { } g) g.playing = true; }
        public void Stop() { if (RT.G(this) is { } g) g.playing = false; }
        public void Evaluate(float deltaTime) => RT.Evaluate(this, deltaTime, deltaTime == 0);
        public DirectorUpdateMode GetTimeUpdateMode() => RT.G(this)?.updateMode ?? DirectorUpdateMode.GameTime;
        public void SetTimeUpdateMode(DirectorUpdateMode value) { if (RT.G(this) is { } g) g.updateMode = value; }
        public IExposedPropertyTable GetResolver() => RT.G(this)?.resolver;
        public void SetResolver(IExposedPropertyTable value) { if (RT.G(this) is { } g) g.resolver = value; }
        public int GetPlayableCount() => RT.G(this) is { } g ? g.nodes.Count(p => RT.N(RT.HandleOf((int)(long)p)) != null) : 0;
        public int GetRootPlayableCount() => Roots().Count();
        IEnumerable<PlayableHandle> Roots() => RT.G(this) is { } g ? g.nodes.Select(p => RT.HandleOf((int)(long)p)).Where(h => RT.N(h) is { } n && n.outputs.Count == 0) : Enumerable.Empty<PlayableHandle>();
        internal void SynchronizeEvaluation(PlayableGraph playable) { }
        public int GetOutputCount() => Outputs().Count();
        internal PlayableHandle CreatePlayableHandle() => RT.CreateNode(this, typeof(Playable));
        internal bool CreateScriptOutputInternal(string name, out PlayableOutputHandle handle) { handle = RT.CreateOutput(this, typeof(ScriptPlayableOutput), name); return handle.IsValid(); }
        internal PlayableHandle GetRootPlayableInternal(int index) { var l = Roots().ToList(); return index >= 0 && index < l.Count ? l[index] : PlayableHandle.Null; }
        internal void DestroyOutputInternal(PlayableOutputHandle handle) { if (RT.O(handle) is { } o) { o.alive = false; o.version++; } }
        internal bool IsMatchFrameRateEnabled() => false;
        internal void EnableMatchFrameRate(FrameRate frameRate) { }
        internal void DisableMatchFrameRate() { }
        internal FrameRate GetFrameRate() => default;
        public string GetEditorName() => RT.G(this)?.name ?? "";
    }

    public partial struct PlayableOutputHandle
    {
        internal IntPtr m_Handle;
        internal uint m_Version;
        internal bool IsPlayableOutputOfType<T>() => RT.O(this)?.type == typeof(T);
        public override int GetHashCode() => m_Handle.GetHashCode() ^ m_Version.GetHashCode();
        public static bool operator ==(PlayableOutputHandle lhs, PlayableOutputHandle rhs) => lhs.m_Handle == rhs.m_Handle && lhs.m_Version == rhs.m_Version;
        public static bool operator !=(PlayableOutputHandle lhs, PlayableOutputHandle rhs) => !(lhs == rhs);
        public override bool Equals(object p) => p is PlayableOutputHandle h && this == h;
        public bool Equals(PlayableOutputHandle other) => this == other;
        internal static bool CompareVersion(PlayableOutputHandle lhs, PlayableOutputHandle rhs) => lhs == rhs;
        internal bool IsNull() => m_Handle == IntPtr.Zero;
        internal bool IsValid() => RT.O(this) != null;
        internal Type GetPlayableOutputType() => RT.O(this)?.type;
        internal Object GetReferenceObject() => RT.O(this)?.reference;
        internal void SetReferenceObject(Object target) { if (RT.O(this) is { } o) o.reference = target; }
        internal Object GetUserData() => RT.O(this)?.userData;
        internal void SetUserData(Object target) { if (RT.O(this) is { } o) o.userData = target; }
        internal PlayableHandle GetSourcePlayable() => RT.O(this)?.source ?? PlayableHandle.Null;
        internal void SetSourcePlayable(PlayableHandle target, int port) { if (RT.O(this) is { } o) { o.source = target; o.sourcePort = port; } }
        internal int GetSourceOutputPort() => RT.O(this)?.sourcePort ?? 0;
        internal float GetWeight() => RT.O(this)?.weight ?? 0;
        internal void SetWeight(float weight) { if (RT.O(this) is { } o) o.weight = weight; }
        internal void PushNotification(PlayableHandle origin, INotification notification, object context)
        {
            if (RT.O(this) is { } o) foreach (var r in o.receivers.ToArray()) r.OnNotify(new Playable(origin), notification, context);
        }
        internal INotificationReceiver[] GetNotificationReceivers() => RT.O(this)?.receivers.ToArray() ?? Array.Empty<INotificationReceiver>();
        internal void AddNotificationReceiver(INotificationReceiver receiver) { if (RT.O(this) is { } o && receiver != null && !o.receivers.Contains(receiver)) o.receivers.Add(receiver); }
        internal void RemoveNotificationReceiver(INotificationReceiver receiver) => RT.O(this)?.receivers.Remove(receiver);
        internal string GetEditorName() => RT.O(this)?.name ?? "";
        internal int GetSourceInputPort() => RT.O(this)?.sourceInputPort ?? 0;
        internal void SetSourceInputPort(int v) { if (RT.O(this) is { } o) o.sourceInputPort = v; }
        public static PlayableOutputHandle Null => default;
    }

    public partial struct PlayableOutput
    {
        PlayableOutputHandle m_Handle;
        internal PlayableOutput(PlayableOutputHandle handle) { m_Handle = handle; }
        public PlayableOutputHandle GetHandle() => m_Handle;
        public bool IsPlayableOutputOfType<T>() where T : struct, IPlayableOutput => m_Handle.IsPlayableOutputOfType<T>();
        public Type GetPlayableOutputType() => m_Handle.GetPlayableOutputType();
        public bool Equals(PlayableOutput other) => m_Handle == other.m_Handle;
        public static PlayableOutput Null => default;
    }

    public static partial class PlayableOutputExtensions
    {
        public static bool IsOutputNull<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().IsNull();
        public static bool IsOutputValid<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().IsValid();
        public static Object GetReferenceObject<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetReferenceObject();
        public static void SetReferenceObject<U>(this U output, Object value) where U : struct, IPlayableOutput => output.GetHandle().SetReferenceObject(value);
        public static Object GetUserData<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetUserData();
        public static void SetUserData<U>(this U output, Object value) where U : struct, IPlayableOutput => output.GetHandle().SetUserData(value);
        public static Playable GetSourcePlayable<U>(this U output) where U : struct, IPlayableOutput => new Playable(output.GetHandle().GetSourcePlayable());
        public static void SetSourcePlayable<U, V>(this U output, V value) where U : struct, IPlayableOutput where V : struct, IPlayable => output.GetHandle().SetSourcePlayable(value.GetHandle(), 0);
        public static void SetSourcePlayable<U, V>(this U output, V value, int port) where U : struct, IPlayableOutput where V : struct, IPlayable => output.GetHandle().SetSourcePlayable(value.GetHandle(), port);
        public static int GetSourceOutputPort<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetSourceOutputPort();
        public static float GetWeight<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetWeight();
        public static void SetWeight<U>(this U output, float value) where U : struct, IPlayableOutput => output.GetHandle().SetWeight(value);
        public static void PushNotification<U>(this U output, Playable origin, INotification notification, object context = null) where U : struct, IPlayableOutput => output.GetHandle().PushNotification(origin.GetHandle(), notification, context);
        public static INotificationReceiver[] GetNotificationReceivers<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetNotificationReceivers();
        public static void AddNotificationReceiver<U>(this U output, INotificationReceiver receiver) where U : struct, IPlayableOutput => output.GetHandle().AddNotificationReceiver(receiver);
        public static void RemoveNotificationReceiver<U>(this U output, INotificationReceiver receiver) where U : struct, IPlayableOutput => output.GetHandle().RemoveNotificationReceiver(receiver);
        [Obsolete] public static int GetSourceInputPort<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetSourceInputPort();
        [Obsolete] public static void SetSourceInputPort<U>(this U output, int value) where U : struct, IPlayableOutput => output.GetHandle().SetSourceInputPort(value);
        [Obsolete] public static void SetSourceOutputPort<U>(this U output, int value) where U : struct, IPlayableOutput { var h = output.GetHandle(); h.SetSourcePlayable(h.GetSourcePlayable(), value); }
    }

    public partial struct ScriptPlayableOutput
    {
        PlayableOutputHandle m_Handle;
        public static ScriptPlayableOutput Create(PlayableGraph graph, string name)
            => graph.CreateScriptOutputInternal(name, out var h) ? new ScriptPlayableOutput(h) : Null;
        internal ScriptPlayableOutput(PlayableOutputHandle handle) { m_Handle = handle; }
        public PlayableOutputHandle GetHandle() => m_Handle;
        public static implicit operator PlayableOutput(ScriptPlayableOutput output) => new PlayableOutput(output.m_Handle);
        public static explicit operator ScriptPlayableOutput(PlayableOutput output) => new ScriptPlayableOutput(output.GetHandle());
        public static ScriptPlayableOutput Null => default;
    }

    public static partial class ScriptPlayableBinding
    {
        public static PlayableBinding Create(string name, Object key, Type type)
            => PlayableBinding.CreateInternal(name, key, type, (g, n) => ScriptPlayableOutput.Create(g, n));
    }
}

namespace UnityEngine.Audio
{
    using UnityEngine.Playables;

    // AudioClipPlayables reached from an AudioPlayableOutput play through its AudioSource (MiniUnity.Audio.PlayableAudio): node time is
    // the clip position, volume × the input weights on the way (AudioMixerPlayable can normalize them) is the gain, plus the stereo pan.
    public partial struct AudioClipPlayable
    {
        internal sealed class Data { public AudioClip clip; public bool loop; public float volume = 1, pan, spatialBlend; public double startDelay, pauseDelay; }
        PlayableHandle m_Handle;
        public static AudioClipPlayable Create(PlayableGraph graph, AudioClip clip, bool looping)
        {
            var h = RT.CreateNode(graph, typeof(AudioClipPlayable));
            var p = new AudioClipPlayable(h);
            if (RT.N(h) is { } n) { n.extra = new Data { clip = clip, loop = looping }; if (clip != null) n.duration = clip.length; }
            return p;
        }
        internal AudioClipPlayable(PlayableHandle handle) { m_Handle = handle; }
        Data D => RT.N(m_Handle)?.extra as Data ?? new Data();
        public PlayableHandle GetHandle() => m_Handle;
        public static implicit operator Playable(AudioClipPlayable playable) => new Playable(playable.m_Handle);
        public static explicit operator AudioClipPlayable(Playable playable) => new AudioClipPlayable(playable.GetHandle());
        public bool Equals(AudioClipPlayable other) => m_Handle == other.m_Handle;
        public AudioClip GetClip() => D.clip;
        public void SetClip(AudioClip value) { D.clip = value; if (RT.N(m_Handle) is { } n && value != null) n.duration = value.length; }
        public bool GetLooped() => D.loop;
        public void SetLooped(bool value) => D.loop = value;
        internal float GetVolume() => D.volume;
        internal void SetVolume(float value) => D.volume = Mathf.Clamp01(value);
        internal float GetStereoPan() => D.pan;
        internal void SetStereoPan(float value) => D.pan = Mathf.Clamp(value, -1, 1);
        internal float GetSpatialBlend() => D.spatialBlend;
        internal void SetSpatialBlend(float value) => D.spatialBlend = Mathf.Clamp01(value);
        public bool IsChannelPlaying() => MiniUnity.Audio.PlayableAudio.IsPlaying(m_Handle);
        public double GetStartDelay() => D.startDelay;
        internal void SetStartDelay(double value) => D.startDelay = Math.Max(0, value);
        public double GetPauseDelay() => D.pauseDelay;
        internal void GetPauseDelay(double value) => D.pauseDelay = Math.Max(0, value);
        public void Seek(double startTime, double startDelay) => Seek(startTime, startDelay, 0);
        public void Seek(double startTime, double startDelay, double duration)
        {
            SetStartDelay(startDelay);
            m_Handle.SetTime(startTime);
            if (duration > 0) { m_Handle.SetDuration(duration + startTime); D.pauseDelay = startDelay + duration; }
            m_Handle.Play();
        }
    }

    public partial struct AudioMixerPlayable
    {
        PlayableHandle m_Handle;
        public static AudioMixerPlayable Create(PlayableGraph graph, int inputCount = 0, bool normalizeInputVolumes = false)
        {
            var p = new AudioMixerPlayable(RT.CreateNode(graph, typeof(AudioMixerPlayable), inputCount));
            if (RT.N(p.m_Handle) is { } n) n.extra = normalizeInputVolumes;
            return p;
        }
        internal AudioMixerPlayable(PlayableHandle handle) { m_Handle = handle; }
        public PlayableHandle GetHandle() => m_Handle;
        public static implicit operator Playable(AudioMixerPlayable playable) => new Playable(playable.m_Handle);
        public static explicit operator AudioMixerPlayable(Playable playable) => new AudioMixerPlayable(playable.GetHandle());
        public bool Equals(AudioMixerPlayable other) => m_Handle == other.m_Handle;
    }

    public partial struct AudioPlayableOutput
    {
        PlayableOutputHandle m_Handle;
        public static AudioPlayableOutput Create(PlayableGraph graph, string name, AudioSource target)
        {
            var o = new AudioPlayableOutput(RT.CreateOutput(graph, typeof(AudioPlayableOutput), name));
            o.SetTarget(target);
            return o;
        }
        internal AudioPlayableOutput(PlayableOutputHandle handle) { m_Handle = handle; }
        public PlayableOutputHandle GetHandle() => m_Handle;
        public static implicit operator PlayableOutput(AudioPlayableOutput output) => new PlayableOutput(output.m_Handle);
        public static explicit operator AudioPlayableOutput(PlayableOutput output) => new AudioPlayableOutput(output.GetHandle());
        public AudioSource GetTarget() => m_Handle.GetUserData() as AudioSource;
        public void SetTarget(AudioSource value) => m_Handle.SetUserData(value);
        public bool GetEvaluateOnSeek() => RT.O(m_Handle)?.evaluateOnSeek ?? false;
        public void SetEvaluateOnSeek(bool value) { if (RT.O(m_Handle) is { } o) o.evaluateOnSeek = value; }
        public static AudioPlayableOutput Null => default;
    }

    public static partial class AudioPlayableBinding
    {
        public static PlayableBinding Create(string name, Object key)
            => PlayableBinding.CreateInternal(name, key, typeof(AudioSource), (g, n) => AudioPlayableOutput.Create(g, n, null));
    }
}
