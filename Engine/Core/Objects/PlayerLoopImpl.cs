using System;
using System.Collections.Generic;
using UnityEngine.PlayerLoop;

namespace UnityEngine.LowLevel
{
    /// <summary>
    /// The player loop as data: the top-level phases of Unity's loop and their native subsystems (types only, nothing to run). Systems a package or
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

        // Unity 2022.3's native subsystems per phase, in default-loop order: listed (no delegate) so code that inserts its own systems
        // next to one of them (VContainer: after EarlyUpdate.ScriptRunDelayedStartupFrame) finds it
        static readonly Dictionary<Type, string[]> NativeSubsystems = new()
        {
            [typeof(TimeUpdate)] = new[] { "WaitForLastPresentationAndUpdateTime" },
            [typeof(Initialization)] = new[] { "ProfilerStartFrame", "UpdateCameraMotionVectors", "DirectorSampleTime", "AsyncUploadTimeSlicedUpdate", "SynchronizeState",
                "SynchronizeInputs", "XREarlyUpdate" },
            [typeof(EarlyUpdate)] = new[] { "PollPlayerConnection", "GpuTimestamp", "AnalyticsCoreStatsUpdate", "UnityWebRequestUpdate", "ExecuteMainThreadJobs",
                "ProcessMouseInWindow", "ClearIntermediateRenderers", "ClearLines", "PresentBeforeUpdate", "ResetFrameStatsAfterPresent", "UpdateAsyncReadbackManager",
                "UpdateStreamingManager", "UpdateTextureStreamingManager", "UpdatePreloading", "RendererNotifyInvisible", "PlayerCleanupCachedData",
                "UpdateMainGameViewRect", "UpdateCanvasRectTransform", "XRUpdate", "UpdateInputManager", "ProcessRemoteInput", "ScriptRunDelayedStartupFrame",
                "UpdateKinect", "DeliverIosPlatformEvents", "ARCoreUpdate", "DispatchEventQueueEvents", "Physics2DEarlyUpdate",
                "PhysicsResetInterpolatedTransformPosition", "SpriteAtlasManagerUpdate", "PerformanceAnalyticsUpdate" },
            [typeof(FixedUpdate)] = new[] { "ClearLines", "NewInputFixedUpdate", "DirectorFixedSampleTime", "AudioFixedUpdate", "ScriptRunBehaviourFixedUpdate",
                "DirectorFixedUpdate", "LegacyFixedAnimationUpdate", "XRFixedUpdate", "PhysicsFixedUpdate", "Physics2DFixedUpdate", "PhysicsClothFixedUpdate",
                "DirectorFixedUpdatePostPhysics", "ScriptRunDelayedFixedFrameRate" },
            [typeof(PreUpdate)] = new[] { "PhysicsUpdate", "Physics2DUpdate", "PhysicsClothUpdate", "CheckTexFieldInput", "IMGUISendQueuedEvents", "NewInputUpdate",
                "SendMouseEvents", "AIUpdate", "WindUpdate", "UpdateVideo" },
            [typeof(Update)] = new[] { "ScriptRunBehaviourUpdate", "ScriptRunDelayedDynamicFrameRate", "ScriptRunDelayedTasks", "DirectorUpdate" },
            [typeof(PreLateUpdate)] = new[] { "AIUpdatePostScript", "DirectorUpdateAnimationBegin", "LegacyAnimationUpdate", "DirectorUpdateAnimationEnd",
                "DirectorDeferredEvaluate", "UIElementsUpdatePanels", "EndGraphicsJobsAfterScriptUpdate", "ConstraintManagerUpdate", "ParticleSystemBeginUpdateAll",
                "Physics2DLateUpdate", "ScriptRunBehaviourLateUpdate" },
            [typeof(PostLateUpdate)] = new[] { "PlayerSendFrameStarted", "DirectorLateUpdate", "ScriptRunDelayedDynamicFrameRate", "PhysicsSkinnedClothBeginUpdate",
                "UpdateRectTransform", "PlayerUpdateCanvases", "UpdateAudio", "VFXUpdate", "ParticleSystemEndUpdateAll", "EndGraphicsJobsAfterScriptLateUpdate",
                "UpdateCustomRenderTextures", "XRPostLateUpdate", "UpdateAllRenderers", "UpdateLightProbeProxyVolumes", "EnlightenRuntimeUpdate",
                "UpdateAllSkinnedMeshes", "ProcessWebSendMessages", "SortingGroupsUpdate", "UpdateVideoTextures", "UpdateVideo", "DirectorRenderImage",
                "PlayerEmitCanvasGeometry", "PhysicsSkinnedClothFinishUpdate", "FinishFrameRendering", "BatchModeUpdate", "PlayerSendFrameComplete",
                "UpdateCaptureScreenshot", "PresentAfterDraw", "ClearImmediateRenderers", "PlayerSendFramePostPresent", "UpdateResolution", "InputEndFrame",
                "TriggerEndOfFrameCallbacks", "GUIClearEvents", "ShaderHandleErrors", "ResetInputAxis", "ThreadedLoadingDebug", "ProfilerSynchronizeStats",
                "MemoryFrameMaintenance", "ExecuteGameCenterCallbacks", "XRPreEndFrame", "ProfilerEndFrame", "GraphicsWarmupPreloadedShaders" },
        };

        public PlayerLoop() { }

        public static PlayerLoopSystem GetDefaultPlayerLoop()
        {
            var subs = new PlayerLoopSystem[Phases.Length];
            for (int i = 0; i < Phases.Length; i++)
            {
                var list = new List<PlayerLoopSystem>();
                if (NativeSubsystems.TryGetValue(Phases[i], out var names))
                    foreach (var n in names)
                        if (Phases[i].GetNestedType(n) is Type t) list.Add(new PlayerLoopSystem { type = t });
                subs[i] = new PlayerLoopSystem { type = Phases[i], subSystemList = list.ToArray() };
            }
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
