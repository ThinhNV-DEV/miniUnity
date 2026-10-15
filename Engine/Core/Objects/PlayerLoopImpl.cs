using System;
using System.Collections.Generic;
using UnityEngine.PlayerLoop;

namespace UnityEngine.LowLevel
{
    /// <summary>
    /// The player loop as data: the top-level phases of Unity's loop (native subsystems are not listed). Systems a package or
    /// game inserts with SetPlayerLoop (Input System, UniTask…) run at the start of their phase in MiniUnity's loop.
    /// </summary>
    public partial class PlayerLoop
    {
        static PlayerLoopSystem? s_Current;

        internal static readonly Type[] Phases =
        {
            typeof(TimeUpdate), typeof(Initialization), typeof(EarlyUpdate), typeof(FixedUpdate), typeof(PreUpdate), typeof(Update),
            typeof(PreLateUpdate), typeof(PostLateUpdate),
        };

        public PlayerLoop() { }

        public static PlayerLoopSystem GetDefaultPlayerLoop()
        {
            var subs = new PlayerLoopSystem[Phases.Length];
            for (int i = 0; i < Phases.Length; i++) subs[i] = new PlayerLoopSystem { type = Phases[i], subSystemList = Array.Empty<PlayerLoopSystem>() };
            return new PlayerLoopSystem { subSystemList = subs };
        }

        public static PlayerLoopSystem GetCurrentPlayerLoop() => s_Current ?? GetDefaultPlayerLoop();

        public static void SetPlayerLoop(PlayerLoopSystem loop) => s_Current = loop;

        /// <summary>Runs the update delegates registered under one top-level phase (depth first, list order).</summary>
        internal static void RunPhase(Type phase)
        {
            if (s_Current is not PlayerLoopSystem root || root.subSystemList == null) return;
            foreach (var p in root.subSystemList)
                if (p.type == phase) Run(p);
        }

        static void Run(PlayerLoopSystem s)
        {
            if (s.updateDelegate != null)
            {
                try { s.updateDelegate(); }
                catch (Exception e) { MiniUnity.Play.Report(e, "PlayerLoop " + (s.type?.Name ?? "?")); }
            }
            if (s.subSystemList != null)
                foreach (var c in s.subSystemList) Run(c);
        }
    }

    public partial struct PlayerLoopSystem
    {
        public override string ToString() => type?.Name ?? "";
    }
}
