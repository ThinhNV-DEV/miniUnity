// The rendering part of a Play-mode frame, as far as scripts can see it: after LateUpdate every enabled camera (by depth) runs
// OnPreCull → culling (OnBecameVisible for renderers seen for the first time) → OnWillRenderObject → OnPreRender → OnRenderObject →
// OnPostRender, plus the Camera.onPreCull / onPreRender / onPostRender delegates; renderers no camera saw any more get
// OnBecameInvisible; then WaitForEndOfFrame coroutines resume. No pixels are produced here (rasterising every frame would cost
// ~250 ms at 1080p): screenshots, record and Camera.Render still rasterise on demand. Culling only runs for GameObjects whose
// scripts listen to visibility. MU_RENDER_CALLBACKS=0 turns the phase off.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    public static partial class Play
    {
        public static readonly bool RenderCallbacks = Environment.GetEnvironmentVariable("MU_RENDER_CALLBACKS") != "0";
        static readonly HashSet<Renderer> visibleLast = new(), visibleNow = new();
        static readonly Plane[] cullPlanes = new Plane[6];

        static bool Has(MonoBehaviour mb, string name) => Method(mb.GetType(), name) != null;

        static void ResetRender() { visibleLast.Clear(); visibleNow.Clear(); }

        static void RenderPhase()
        {
            if (!RenderCallbacks) return;
            var cams = ObjectRegistry.SceneObjects(typeof(Camera), false).Cast<Camera>()
                .Where(c => c != null && c.enabled && c.gameObject.activeInHierarchy).OrderBy(c => c.depth).ToList();
            var active = Active();
            // renderers whose GameObject listens to visibility (others are never culled here)
            var watched = new List<(Renderer r, List<MonoBehaviour> listeners)>();
            var byGo = new Dictionary<GameObject, List<MonoBehaviour>>();
            List<MonoBehaviour> renderObject = null;
            foreach (var mb in active)
            {
                var t = mb.GetType();
                if (Has(mb, "OnBecameVisible") || Has(mb, "OnBecameInvisible") || Has(mb, "OnWillRenderObject"))
                    (byGo.TryGetValue(mb.gameObject, out var l) ? l : byGo[mb.gameObject] = new()).Add(mb);
                if (Has(mb, "OnRenderObject")) (renderObject ??= new()).Add(mb);
            }
            foreach (var (go, listeners) in byGo)
                foreach (var c in go.m_Components)
                    if (c is Renderer r && !r.m_Destroyed) watched.Add((r, listeners));
            visibleNow.Clear();
            foreach (var cam in cams)
            {
                if (cam.m_Destroyed || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                CameraMessage(cam, "OnPreCull"); Delegate(Camera.onPreCull, cam);
                if (watched.Count > 0)
                {
                    GeometryUtility.CalculateFrustumPlanes(cam, cullPlanes);
                    int mask = cam.cullingMask;
                    foreach (var (r, listeners) in watched)
                    {
                        if (r.m_Destroyed || !r.enabled || !r.gameObject.activeInHierarchy || (mask & (1 << r.gameObject.layer)) == 0) continue;
                        if (!GeometryUtility.TestPlanesAABB(cullPlanes, Visibility.WorldBounds(r))) continue;
                        if (visibleNow.Add(r) && !visibleLast.Contains(r)) foreach (var mb in listeners) if (Alive(mb)) Call(mb, "OnBecameVisible");
                        foreach (var mb in listeners) if (Alive(mb)) { s_CurrentCamera = cam; Call(mb, "OnWillRenderObject"); }
                    }
                }
                s_CurrentCamera = cam;
                CameraMessage(cam, "OnPreRender"); Delegate(Camera.onPreRender, cam);
                if (renderObject != null) foreach (var mb in renderObject) if (Alive(mb)) Call(mb, "OnRenderObject");
                CameraMessage(cam, "OnPostRender"); Delegate(Camera.onPostRender, cam);
                s_CurrentCamera = null;
            }
            foreach (var (r, listeners) in watched)
                if (visibleLast.Contains(r) && !visibleNow.Contains(r)) foreach (var mb in listeners) if (Alive(mb)) Call(mb, "OnBecameInvisible");
            visibleLast.Clear(); visibleLast.UnionWith(visibleNow);
        }

        /// <summary>Camera.current while a camera's render callbacks run.</summary>
        internal static Camera s_CurrentCamera;

        static bool Alive(MonoBehaviour mb) => !mb.m_Destroyed && mb.m_Enabled && mb.gameObject.activeInHierarchy;

        static void CameraMessage(Camera cam, string name)
        {
            foreach (var c in cam.gameObject.m_Components.ToList())
                if (c is MonoBehaviour mb && Alive(mb)) Call(mb, name);
        }

        static void Delegate(Camera.CameraCallback cb, Camera cam)
        {
            if (cb == null) return;
            foreach (Camera.CameraCallback d in cb.GetInvocationList())
                try { d(cam); } catch (Exception e) { Report(e, "Camera callback"); }
        }
    }
}
