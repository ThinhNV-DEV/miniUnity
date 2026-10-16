// OnMouseDown / OnMouseDrag / OnMouseUp / OnMouseUpAsButton / OnMouseEnter / OnMouseOver / OnMouseExit, sent in PreUpdate like
// Unity's SendMouseEvents: the mouse position is cast from every enabled camera (highest depth first, inside its pixel rect,
// against its culling mask) into 3D colliders and, separately, 2D colliders; each of the two hits is tracked on its own
// (Unity keeps a 3D and a 2D slot), and the messages go to every enabled behaviour of the hit collider's GameObject.
// Skipped when no active behaviour has one of these methods; MU_MOUSE_EVENTS=0 turns it off.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiniUnity
{
    public static class MouseEvents
    {
        static readonly string[] Names = { "OnMouseDown", "OnMouseDrag", "OnMouseUp", "OnMouseUpAsButton", "OnMouseEnter", "OnMouseOver", "OnMouseExit" };
        static readonly GameObject[] s_Last = new GameObject[2], s_Down = new GameObject[2];
        static readonly Dictionary<Type, bool> s_Has = new();
        static readonly bool Enabled = Environment.GetEnvironmentVariable("MU_MOUSE_EVENTS") != "0";

        public static void Reset() { Array.Clear(s_Last); Array.Clear(s_Down); }

        static bool Listens(Type t)
        {
            if (s_Has.TryGetValue(t, out var b)) return b;
            return s_Has[t] = Names.Any(n => Play.Method(t, n) != null);
        }

        public static void Send()
        {
            if (!Enabled) return;
            if (!Play.Active().Any(mb => Listens(mb.GetType())) && s_Last.All(x => x == null) && s_Down.All(x => x == null)) return;
            var mouse = Input.mousePosition;
            var hits = Pick(mouse);
            bool down = InputSim.Down(KeyCode.Mouse0), up = InputSim.Up(KeyCode.Mouse0), held = InputSim.Held(KeyCode.Mouse0);
            for (int i = 0; i < 2; i++)
            {
                var hit = hits[i];
                if (down) { if (hit != null) { s_Down[i] = hit; Message(hit, "OnMouseDown"); } }
                else if (held) { if (Alive(s_Down[i])) Message(s_Down[i], "OnMouseDrag"); }
                if (up)
                {
                    if (Alive(s_Down[i]))
                    {
                        if (hit == s_Down[i]) Message(hit, "OnMouseUpAsButton");
                        Message(s_Down[i], "OnMouseUp");
                    }
                    s_Down[i] = null;
                }
                if (hit == s_Last[i]) { if (hit != null) Message(hit, "OnMouseOver"); }
                else
                {
                    if (Alive(s_Last[i])) Message(s_Last[i], "OnMouseExit");
                    if (hit != null) { Message(hit, "OnMouseEnter"); Message(hit, "OnMouseOver"); }
                }
                s_Last[i] = hit;
            }
        }

        static bool Alive(GameObject g) => g != null && !g.m_Destroyed;

        static void Message(GameObject go, string name)
        {
            foreach (var c in go.m_Components.ToList())
                if (c is MonoBehaviour mb && !mb.m_Destroyed && mb.isActiveAndEnabled) Play.Call(mb, name);
        }

        /// <summary>The GameObjects under the mouse: [0] from 3D colliders, [1] from 2D colliders.</summary>
        static GameObject[] Pick(Vector3 mouse)
        {
            var res = new GameObject[2];
            var cams = Camera.allCameras.Where(c => c != null && c.isActiveAndEnabled && c.targetTexture == null).OrderByDescending(c => c.depth).ToList();
            foreach (var cam in cams)
            {
                var r = cam.pixelRect;
                if (mouse.x < r.xMin || mouse.x > r.xMax || mouse.y < r.yMin || mouse.y > r.yMax) continue;
                var ray = cam.ScreenPointToRay(mouse);
                float dist = cam.farClipPlane - cam.nearClipPlane;
                int mask = cam.cullingMask;
                if (res[0] == null && Physics.Raycast(ray, out var h3, dist, mask)) res[0] = h3.collider.gameObject;
                if (res[1] == null)
                {
                    var h2 = Physics2D.GetRayIntersection(ray, dist, mask);
                    if (h2.collider != null) res[1] = h2.collider.gameObject;
                }
                if (res[0] != null && res[1] != null) break;
            }
            return res;
        }
    }
}
