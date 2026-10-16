// Managed PlayableGraph: the part of Unity's native Playables runtime the Timeline package needs in edit mode.
// A graph is a table of nodes (behaviour, inputs + weights, local time, play state, traversal mode) and outputs
// (type, reference object, user data, source playable + port). PlayableDirector.Evaluate builds the graph from its
// PlayableAsset, binds every output's user data from the director's scene bindings, sets the root time and runs
// PrepareFrame (parent first) / ProcessFrame (inputs first) over each output's subgraph – the order Unity documents.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    public static class PlayableRuntime
    {
        public sealed class Node
        {
            public uint version = 1;
            public bool alive = true;
            public int graph;
            public Type type;
            public object behaviour;
            public readonly List<(PlayableHandle src, float weight)> inputs = new();
            public readonly List<PlayableHandle> outputs = new();
            public double time, prevTime, duration = double.MaxValue, speed = 1, delay;
            public float leadTime;
            public PlayState state = PlayState.Playing;
            public bool done, propagate, playingSeen;
            public PlayableTraversalMode traversal = PlayableTraversalMode.Mix;
            public DirectorWrapMode wrap = DirectorWrapMode.Loop;
            public ulong preparedFrame;
            public object extra;   // per-type payload (AudioClip, loop flag…)
        }

        public sealed class Output
        {
            public uint version = 1;
            public bool alive = true;
            public int graph;
            public Type type;
            public string name;
            public Object reference, userData;
            public PlayableHandle source;
            public int sourcePort, sourceInputPort;
            public float weight = 1;
            public bool evaluateOnSeek;
            public object extra;   // per-type state (animation output: bound hierarchy)
            public readonly List<INotificationReceiver> receivers = new();
        }

        public sealed class Graph
        {
            public uint version = 1;
            public bool alive = true, started, playing;
            public string name = "";
            public readonly List<IntPtr> nodes = new();
            public readonly List<IntPtr> outputs = new();
            public IExposedPropertyTable resolver;
            public object owner;
            public DirectorUpdateMode updateMode = DirectorUpdateMode.GameTime;
            public ulong frame;
        }

        /// <summary>Graphs started with PlayableGraph.Play: Play mode evaluates them every frame (GameTime / UnscaledGameTime).</summary>
        public static IEnumerable<PlayableGraph> PlayingGraphs()
        {
            for (int i = 1; i < s_Graphs.Count; i++) if (s_Graphs[i] is { alive: true, playing: true }) yield return GraphOf(i);
        }

        /// <summary>Called by MiniUnity.Play after Update: every playing graph advances and evaluates once.</summary>
        public static void Tick(float scaledDt, float unscaledDt)
        {
            foreach (var g in PlayingGraphs().ToList())
            {
                var x = G(g);
                if (x == null || x.updateMode == DirectorUpdateMode.Manual) continue;
                if (x.owner is PlayableDirector) continue;   // directors drive their own graphs
                try { Evaluate(g, x.updateMode == DirectorUpdateMode.UnscaledGameTime ? unscaledDt : scaledDt, false); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static readonly List<Node> s_Nodes = new() { null };
        static readonly List<Output> s_Outputs = new() { null };
        static readonly List<Graph> s_Graphs = new() { null };

        // ---------------------------------------------------------------- lookup
        public static Node N(PlayableHandle h)
        {
            long i = (long)h.m_Handle;
            if (i <= 0 || i >= s_Nodes.Count) return null;
            var n = s_Nodes[(int)i];
            return n != null && n.alive && n.version == h.m_Version ? n : null;
        }
        public static Output O(PlayableOutputHandle h)
        {
            long i = (long)h.m_Handle;
            if (i <= 0 || i >= s_Outputs.Count) return null;
            var o = s_Outputs[(int)i];
            return o != null && o.alive && o.version == h.m_Version ? o : null;
        }
        public static Graph G(PlayableGraph g)
        {
            long i = (long)g.m_Handle;
            if (i <= 0 || i >= s_Graphs.Count) return null;
            var x = s_Graphs[(int)i];
            return x != null && x.alive && x.version == g.m_Version ? x : null;
        }

        public static PlayableGraph CreateGraph(string name)
        {
            s_Graphs.Add(new Graph { name = name ?? "" });
            return new PlayableGraph { m_Handle = (IntPtr)(s_Graphs.Count - 1), m_Version = 1 };
        }

        public static PlayableHandle CreateNode(PlayableGraph graph, Type type, int inputCount = 0)
        {
            var g = G(graph);
            if (g == null) return PlayableHandle.Null;
            var n = new Node { graph = (int)(long)graph.m_Handle, type = type ?? typeof(Playable) };
            for (int i = 0; i < inputCount; i++) n.inputs.Add((PlayableHandle.Null, 0));
            s_Nodes.Add(n);
            var h = new PlayableHandle { m_Handle = (IntPtr)(s_Nodes.Count - 1), m_Version = n.version };
            g.nodes.Add(h.m_Handle);
            return h;
        }

        public static PlayableOutputHandle CreateOutput(PlayableGraph graph, Type type, string name)
        {
            var g = G(graph);
            if (g == null) return PlayableOutputHandle.Null;
            var o = new Output { graph = (int)(long)graph.m_Handle, type = type, name = name ?? "" };
            s_Outputs.Add(o);
            var h = new PlayableOutputHandle { m_Handle = (IntPtr)(s_Outputs.Count - 1), m_Version = o.version };
            g.outputs.Add(h.m_Handle);
            return h;
        }

        public static PlayableHandle HandleOf(int index) => index > 0 && index < s_Nodes.Count && s_Nodes[index] != null
            ? new PlayableHandle { m_Handle = (IntPtr)index, m_Version = s_Nodes[index].version } : PlayableHandle.Null;
        public static PlayableOutputHandle OutputHandleOf(int index) => index > 0 && index < s_Outputs.Count && s_Outputs[index] != null
            ? new PlayableOutputHandle { m_Handle = (IntPtr)index, m_Version = s_Outputs[index].version } : PlayableOutputHandle.Null;

        public static PlayableGraph GraphOf(int index) => index > 0 && index < s_Graphs.Count && s_Graphs[index] != null
            ? new PlayableGraph { m_Handle = (IntPtr)index, m_Version = s_Graphs[index].version } : default;

        public static void DestroyNode(PlayableHandle h)
        {
            var n = N(h);
            if (n == null) return;
            if (n.behaviour is IPlayableBehaviour b) { try { b.OnPlayableDestroy(new Playable(h)); } catch (Exception e) { Debug.LogException(e); } }
            // disconnect from consumers
            foreach (var consumer in n.outputs)
                if (N(consumer) is Node c)
                    for (int i = 0; i < c.inputs.Count; i++) if (c.inputs[i].src == h) c.inputs[i] = (PlayableHandle.Null, c.inputs[i].weight);
            foreach (var (src, _) in n.inputs) if (N(src) is Node s) s.outputs.Remove(h);
            n.alive = false; n.version++;
        }

        public static void DestroyGraph(PlayableGraph graph)
        {
            var g = G(graph);
            if (g == null) return;
            foreach (var p in g.nodes) DestroyNode(HandleOf((int)(long)p));
            foreach (var p in g.outputs) { var o = s_Outputs[(int)(long)p]; if (o != null) { o.alive = false; o.version++; } }
            g.alive = false; g.version++;
        }

        public static bool Connect(PlayableHandle src, int srcPort, PlayableHandle dst, int dstPort)
        {
            var s = N(src); var d = N(dst);
            if (s == null || d == null) return false;
            if (dstPort < 0) dstPort = d.inputs.Count;
            while (d.inputs.Count <= dstPort) d.inputs.Add((PlayableHandle.Null, 0));
            if (N(d.inputs[dstPort].src) is Node old) old.outputs.Remove(dst);
            d.inputs[dstPort] = (src, d.inputs[dstPort].weight);
            if (!s.outputs.Contains(dst)) s.outputs.Add(dst);
            return true;
        }

        public static void Disconnect(PlayableHandle dst, int port)
        {
            var d = N(dst);
            if (d == null || port < 0 || port >= d.inputs.Count) return;
            if (N(d.inputs[port].src) is Node s) s.outputs.Remove(dst);
            d.inputs[port] = (PlayableHandle.Null, d.inputs[port].weight);
        }

        public static void SetTime(PlayableHandle h, double t)
        {
            var n = N(h);
            if (n == null) return;
            n.prevTime = n.time; n.time = t; n.done = false;
            if (n.propagate)
                foreach (var (src, _) in n.inputs) SetTime(src, t);
        }

        // ---------------------------------------------------------------- evaluation
        public static void Evaluate(PlayableGraph graph, float deltaTime, bool seek)
        {
            var g = G(graph);
            if (g == null) return;
            g.frame++;
            if (!g.started)
            {
                g.started = true;
                foreach (var p in g.nodes.ToArray())
                    if (s_Nodes[(int)(long)p] is { alive: true, behaviour: IPlayableBehaviour b } n)
                        try { b.OnGraphStart(new Playable(HandleOf((int)(long)p))); } catch (Exception e) { Debug.LogException(e); }
            }
            // every playing playable advances by deltaTime × its speed × the speeds of the playables it feeds (a paused one stops
            // its subtree); a playable with a duration is done once its time reaches it
            if (deltaTime != 0)
            {
                var advanced = new HashSet<long>();
                foreach (var p in g.nodes)
                    if (s_Nodes[(int)(long)p] is { alive: true } n && n.outputs.Count == 0) Advance(HandleOf((int)(long)p), deltaTime, 1, advanced);
            }

            foreach (var op in g.outputs.ToArray())
            {
                var o = s_Outputs[(int)(long)op];
                if (o == null || !o.alive || N(o.source) == null) continue;
                var oh = OutputHandleOf((int)(long)op);
                var info = Frame(g, deltaTime, seek, o.weight, oh, PlayState.Playing);
                info.m_EffectiveSpeed = (float)(N(o.source)?.speed ?? 1);
                Prepare(o.source, o.sourcePort, info, g.frame);
                Process(o.source, o.sourcePort, info, o.userData, new HashSet<long>());
                if (o.type == typeof(UnityEngine.Audio.AudioPlayableOutput)) Audio.PlayableAudio.Sync(o, oh);
                else if (o.type == typeof(UnityEngine.Animations.AnimationPlayableOutput)) Anim.PlayableAnimation.Evaluate(o, seek);
            }
        }

        static void Advance(PlayableHandle h, double dt, double parentSpeed, HashSet<long> advanced)
        {
            var n = N(h);
            if (n == null || n.state != PlayState.Playing || !advanced.Add((long)h.m_Handle)) return;
            double speed = n.speed * parentSpeed;
            n.prevTime = n.time; n.time += dt * speed;
            if (n.duration != double.MaxValue && n.duration >= 0 && (speed >= 0 ? n.time >= n.duration : n.time <= 0)) n.done = true;
            foreach (var (src, _) in n.inputs) Advance(src, dt, speed, advanced);
        }

        static FrameData Frame(Graph g, float dt, bool seek, float weight, PlayableOutputHandle oh, PlayState st)
        {
            var f = FrameData.Flags.Evaluate;
            if (seek) f |= FrameData.Flags.SeekOccured;
            if (st == PlayState.Playing) f |= FrameData.Flags.EffectivePlayStatePlaying;
            return new FrameData
            {
                m_FrameID = g.frame, m_DeltaTime = dt, m_Weight = weight, m_EffectiveWeight = weight, m_EffectiveSpeed = 1, m_EffectiveParentSpeed = 1,
                m_Flags = f, m_Output = new PlayableOutput(oh),
            };
        }

        static IEnumerable<PlayableHandle> Reached(Node n, int port)
        {
            if (n.traversal == PlayableTraversalMode.Passthrough)
            {
                if (port >= 0 && port < n.inputs.Count) yield return n.inputs[port].src;
                yield break;
            }
            foreach (var (src, _) in n.inputs) yield return src;
        }

        static void Prepare(PlayableHandle h, int port, FrameData info, ulong frame)
        {
            var n = N(h);
            if (n == null) return;
            var p = new Playable(h);
            bool playing = n.state == PlayState.Playing;
            var local = info;
            local.m_Flags = playing ? info.m_Flags | FrameData.Flags.EffectivePlayStatePlaying : info.m_Flags & ~FrameData.Flags.EffectivePlayStatePlaying;
            if (n.preparedFrame != frame)
            {
                n.preparedFrame = frame;
                if (n.behaviour is IPlayableBehaviour b)
                {
                    try
                    {
                        if (playing && !n.playingSeen) { n.playingSeen = true; b.OnBehaviourPlay(p, local); }
                        else if (!playing && n.playingSeen) { n.playingSeen = false; b.OnBehaviourPause(p, local); }
                        b.PrepareFrame(p, local);
                    }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            if (!playing) return;   // paused subtrees are not evaluated
            var ins = n.inputs;
            int i = 0;
            foreach (var src in Reached(n, port))
            {
                var child = info;
                int idx = n.traversal == PlayableTraversalMode.Passthrough ? port : i;
                if (idx >= 0 && idx < ins.Count) { child.m_Weight = ins[idx].weight; child.m_EffectiveWeight = info.m_EffectiveWeight * ins[idx].weight; }
                child.m_EffectiveParentSpeed = info.m_EffectiveSpeed; child.m_EffectiveSpeed = info.m_EffectiveSpeed * (float)(N(src)?.speed ?? 1);
                Prepare(src, 0, child, frame);
                i++;
            }
        }

        static void Process(PlayableHandle h, int port, FrameData info, object playerData, HashSet<long> seen)
        {
            var n = N(h);
            if (n == null || !seen.Add((long)h.m_Handle)) return;
            if (n.state != PlayState.Playing) return;
            int i = 0;
            foreach (var src in Reached(n, port))
            {
                var child = info;
                int idx = n.traversal == PlayableTraversalMode.Passthrough ? port : i;
                if (idx >= 0 && idx < n.inputs.Count) { child.m_Weight = n.inputs[idx].weight; child.m_EffectiveWeight = info.m_EffectiveWeight * n.inputs[idx].weight; }
                child.m_EffectiveParentSpeed = info.m_EffectiveSpeed; child.m_EffectiveSpeed = info.m_EffectiveSpeed * (float)(N(src)?.speed ?? 1);
                Process(src, 0, child, playerData, seen);
                i++;
            }
            if (n.behaviour is IPlayableBehaviour b)
            {
                try { b.ProcessFrame(new Playable(h), info, playerData); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
