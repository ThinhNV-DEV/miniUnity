// Legacy input (UnityEngine.Input) for MiniUnity's player loop. Virtual axes and buttons come from
// ProjectSettings/InputManager.asset with Unity's semantics: key / mouse-button axes move toward their target at
// `sensitivity` and fall back at `gravity` units per second, `snap` jumps to 0 when the opposite direction is pressed,
// `invert`, joystick dead zones, mouse-movement axes = delta · sensitivity, several entries with one name → largest
// magnitude wins; unknown axis / button / key names throw like Unity. Device state is scripted per frame (`ua play
// --input`): keys (KeyCode or Unity key names), mouse position / drags / scroll, touches, joystick axes and buttons, text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace MiniUnity
{
    /// <summary>
    /// Scripted input for `ua play --input`, comma separated, frames are 1-based player-loop frames:
    /// "Space@10", "left shift@5-20" (held 5..20), "mouse0@5", "Jump@12" (presses the axis' positive button),
    /// "Horizontal=0.5@30-60" (axis value override), "mouse=(400,300)" or "mouse=(400,300)@10", "mouse=(0,0)-(200,100)@10-20" (drag),
    /// "scroll=(0,1)@15", "touch0=(100,200)@5-9", "touch1=(10,10)-(90,10)@20-30", "joyaxis1=-1@5-30", "joy2axis0=0.5@5-9",
    /// "joystick button 0@7", "text=hello@40".
    /// Input System devices (com.unity.inputsystem), connected when the script uses them: "pad.leftStick=(1,0)@10-30",
    /// "pad.buttonSouth@5" (aliases a / cross, b, x, y, …), "pad.rightTrigger=0.5@8-12", "pad.dpad=(0,1)@3"; "joy.stick=(0,-1)@4-9",
    /// "joy.trigger@6", "joy.hat=(1,0)@2".
    /// </summary>
    public sealed class InputScript
    {
        sealed class Span { public string Kind, Name; public int From, To, Finger, Joy = -1, Axis; public float Value = 1; public Vector2 A, B; public string Text; }
        readonly List<Span> spans = new();
        public Vector3 MousePosition;
        public void Clear() { spans.Clear(); MousePosition = default; InputSim.Reset(); }

        static IEnumerable<string> Items(string spec)
        {
            int depth = 0, start = 0;
            for (int i = 0; i <= spec.Length; i++)
            {
                if (i < spec.Length && spec[i] == '(') depth++;
                else if (i < spec.Length && spec[i] == ')') depth--;
                else if (i == spec.Length || (depth == 0 && (spec[i] == ',' || spec[i] == ';')))
                {
                    var t = spec.Substring(start, i - start).Trim();
                    if (t.Length > 0) yield return t;
                    start = i + 1;
                }
            }
        }

        static float F(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);
        static Vector2 P(string s) { var t = s.Trim().TrimStart('(').TrimEnd(')').Split(',', ' ', '|').Where(x => x.Length > 0).ToArray(); return new Vector2(F(t[0]), F(t[1])); }

        public void Parse(string spec)
        {
            foreach (var p in Items(spec))
            {
                int at = p.LastIndexOf('@');
                string body = at >= 0 && !p.Substring(at).Contains(')') ? p[..at] : p, range = at >= 0 && !p.Substring(at).Contains(')') ? p[(at + 1)..] : null;
                int from = 1, to = int.MaxValue;
                if (range != null)
                {
                    var fr = range.Split('-');
                    from = int.Parse(fr[0]); to = fr.Length > 1 ? int.Parse(fr[1]) : from;
                }
                int eq = body.IndexOf('=');
                string name = eq >= 0 ? body[..eq].Trim() : body.Trim(), val = eq >= 0 ? body[(eq + 1)..].Trim() : null;
                var lname = name.ToLowerInvariant();
                if (lname == "mouse" && val != null)
                {
                    var parts = val.Split(")-(");
                    var a = P(parts[0]); var b = parts.Length > 1 ? P(parts[1]) : a;
                    if (range == null) { MousePosition = new Vector3(a.x, a.y, 0); continue; }
                    spans.Add(new Span { Kind = "mouse", From = from, To = to, A = a, B = b });
                }
                else if (lname == "scroll" && val != null) spans.Add(new Span { Kind = "scroll", From = from, To = to, A = P(val) });
                else if (lname.StartsWith("touch") && val != null)
                {
                    var parts = val.Split(")-(");
                    var a = P(parts[0]); var b = parts.Length > 1 ? P(parts[1]) : a;
                    spans.Add(new Span { Kind = "touch", Finger = lname.Length > 5 ? int.Parse(lname[5..]) : 0, From = from, To = to == int.MaxValue ? from : to, A = a, B = b });
                }
                else if ((lname.StartsWith("pad.") || lname.StartsWith("joy.")) && lname.Length > 4)
                {
                    bool pad = lname[0] == 'p';
                    var control = lname[4..];
                    var v = val == null ? Vector2.right : val.Contains('(') ? P(val) : new Vector2(F(val), 0);
                    if (!(pad ? InputSim.PadControls : InputSim.JoyControls).Contains(control))
                        throw new ArgumentException($"--input: unknown {(pad ? "gamepad" : "joystick")} control '{control}' (one of {string.Join(", ", pad ? InputSim.PadControls : InputSim.JoyControls)})");
                    spans.Add(new Span { Kind = pad ? "pad" : "joy", Name = control, From = from, To = to, A = v });
                    if (pad) InputSim.PadUsed = true; else InputSim.JoystickUsed = true;
                }
                else if (lname.StartsWith("joy") && lname.Contains("axis") && val != null)
                {
                    int ai = lname.IndexOf("axis");
                    int joy = ai > 3 ? int.Parse(lname[3..ai]) : 0;
                    spans.Add(new Span { Kind = "joyaxis", Joy = joy, Axis = int.Parse(lname[(ai + 4)..]), From = from, To = to, Value = F(val) });
                }
                else if (lname == "text" && val != null) spans.Add(new Span { Kind = "text", From = from, To = from, Text = val });
                else if (val != null) spans.Add(new Span { Kind = "axis", Name = name, From = from, To = to, Value = F(val) });
                else spans.Add(new Span { Kind = "key", Name = name, From = from, To = to });
            }
        }

        // ------------------------------------------------------------------ state at a frame
        public void Fill(InputSim.Frame f, int frame)
        {
            f.Mouse = MousePosition;
            foreach (var s in spans)
            {
                bool on = frame >= s.From && frame <= s.To;
                switch (s.Kind)
                {
                    case "key":
                        if (!on) break;
                        if (InputSim.KeyOf(s.Name) is KeyCode k) f.Keys.Add(k);
                        else foreach (var b in InputSim.PositiveButtons(s.Name)) f.Keys.Add(b);
                        break;
                    case "axis": if (on) f.AxisOverride[s.Name] = s.Value; break;
                    case "mouse":
                        if (frame >= s.From)
                        {
                            float t = s.To == s.From || s.To == int.MaxValue ? 1 : Mathf.Clamp01((frame - s.From) / (float)(s.To - s.From));
                            var p = Vector2.Lerp(s.A, s.B, t); f.Mouse = new Vector3(p.x, p.y, 0);
                        }
                        break;
                    case "scroll": if (on) f.Scroll += s.A; break;
                    case "touch":
                        if (frame >= s.From && frame <= s.To + 1)
                        {
                            float t = s.To == s.From ? 1 : Mathf.Clamp01((frame - s.From) / (float)(s.To - s.From));
                            f.Touches[s.Finger] = (Vector2.Lerp(s.A, s.B, t), frame > s.To);
                        }
                        break;
                    case "joyaxis": if (on) f.JoyAxes[(s.Joy, s.Axis)] = s.Value; break;
                    case "pad": if (on) f.Pad[s.Name] = s.A; break;
                    case "joy": if (on) f.Joy[s.Name] = s.A; break;
                    case "text": if (on) f.Text += s.Text; break;
                }
            }
        }
    }

    public static class InputSim
    {
        public sealed class Frame
        {
            public int Number = -1;
            public readonly HashSet<KeyCode> Keys = new();
            public readonly Dictionary<string, float> AxisOverride = new(StringComparer.Ordinal);
            public readonly Dictionary<int, (Vector2 pos, bool ended)> Touches = new();
            public readonly Dictionary<(int joy, int axis), float> JoyAxes = new();
            /// <summary>Input System gamepad / joystick controls (lower-case name → value; buttons are (1, 0)).</summary>
            public readonly Dictionary<string, Vector2> Pad = new(StringComparer.Ordinal), Joy = new(StringComparer.Ordinal);
            public Vector3 Mouse; public Vector2 Scroll; public string Text = "";
        }

        public sealed class Axis
        {
            public string Name, Pos, Neg, AltPos, AltNeg; public KeyCode? PosK, NegK, AltPosK, AltNegK;
            public float Gravity, Dead, Sensitivity; public bool Snap, Invert; public int Type, AxisIndex, JoyNum;
            public float Value;
        }

        public static Frame Cur = new(), Prev = new();
        static List<Axis> axes; static bool loaded;
        static readonly Dictionary<int, (int tapCount, Vector2 last)> touchInfo = new();
        static bool reset;
        public static List<Axis> Axes { get { Load(); return axes; } }

        public static void Reset() { Cur = new Frame(); Prev = new Frame(); axes = null; loaded = false; touchInfo.Clear(); PadUsed = JoystickUsed = false; }

        /// <summary>The scripted input uses a gamepad / joystick: the Input System backend connects one.</summary>
        public static bool PadUsed, JoystickUsed;
        /// <summary>GamepadState button bits (GamepadButton) by control name and alias, then the analog controls.</summary>
        internal static readonly Dictionary<string, int> PadButtonBits = new(StringComparer.Ordinal)
        {
            ["dpad/up"] = 0, ["dpad/down"] = 1, ["dpad/left"] = 2, ["dpad/right"] = 3,
            ["buttonnorth"] = 4, ["y"] = 4, ["triangle"] = 4, ["buttoneast"] = 5, ["b"] = 5, ["circle"] = 5,
            ["buttonsouth"] = 6, ["a"] = 6, ["cross"] = 6, ["buttonwest"] = 7, ["x"] = 7, ["square"] = 7,
            ["leftstickpress"] = 8, ["rightstickpress"] = 9, ["leftshoulder"] = 10, ["rightshoulder"] = 11, ["start"] = 12, ["select"] = 13,
        };
        internal static readonly HashSet<string> PadControls = new(PadButtonBits.Keys.Concat(new[] { "leftstick", "rightstick", "lefttrigger", "righttrigger", "dpad" }), StringComparer.Ordinal);
        internal static readonly HashSet<string> JoyControls = new(StringComparer.Ordinal) { "stick", "trigger", "hat" };

        static void Load()
        {
            if (loaded) return;
            loaded = true; axes = new List<Axis>();
            try
            {
                var path = System.IO.Path.Combine(AssetIndex.Root ?? ".", "ProjectSettings/InputManager.asset");
                if (!System.IO.File.Exists(path)) return;
                var m = Yaml.YamlReader.ReadFile(path).FirstOrDefault()?.Body as Yaml.YMap;
                if (m?["m_Axes"] is not Yaml.YSeq seq) return;
                string S(Yaml.YMap a, string k) => (a[k] as Yaml.YScalar)?.Value ?? "";
                float Fl(Yaml.YMap a, string k) => a[k] is Yaml.YScalar s && s.Value.Length > 0 ? s.F : 0;
                foreach (var it in seq.Items)
                {
                    if (it is not Yaml.YMap a) continue;
                    var ax = new Axis
                    {
                        Name = S(a, "m_Name"), Pos = S(a, "positiveButton"), Neg = S(a, "negativeButton"), AltPos = S(a, "altPositiveButton"), AltNeg = S(a, "altNegativeButton"),
                        Gravity = Fl(a, "gravity"), Dead = Fl(a, "dead"), Sensitivity = Fl(a, "sensitivity"), Snap = Fl(a, "snap") != 0, Invert = Fl(a, "invert") != 0,
                        Type = (int)Fl(a, "type"), AxisIndex = (int)Fl(a, "axis"), JoyNum = (int)Fl(a, "joyNum"),
                    };
                    ax.PosK = KeyOf(ax.Pos); ax.NegK = KeyOf(ax.Neg); ax.AltPosK = KeyOf(ax.AltPos); ax.AltNegK = KeyOf(ax.AltNeg);
                    axes.Add(ax);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ key names
        static Dictionary<string, KeyCode> names;
        static Dictionary<KeyCode, string> unityNames;
        static void BuildNames()
        {
            if (names != null) return;
            names = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase); unityNames = new();
            void Add(KeyCode k, string n) { names[n] = k; unityNames.TryAdd(k, n); }
            var special = new Dictionary<string, string>
            {
                ["UpArrow"] = "up", ["DownArrow"] = "down", ["LeftArrow"] = "left", ["RightArrow"] = "right", ["PageUp"] = "page up", ["PageDown"] = "page down",
                ["CapsLock"] = "caps lock", ["ScrollLock"] = "scroll lock", ["RightShift"] = "right shift", ["LeftShift"] = "left shift", ["RightControl"] = "right ctrl",
                ["LeftControl"] = "left ctrl", ["RightAlt"] = "right alt", ["LeftAlt"] = "left alt", ["RightCommand"] = "right cmd", ["LeftCommand"] = "left cmd",
                ["LeftWindows"] = "left super", ["RightWindows"] = "right super", ["AltGr"] = "alt gr", ["Print"] = "print screen", ["SysReq"] = "sys req",
                ["KeypadPeriod"] = "[.]", ["KeypadDivide"] = "[/]", ["KeypadMultiply"] = "[*]", ["KeypadMinus"] = "[-]", ["KeypadPlus"] = "[+]", ["KeypadEnter"] = "enter", ["KeypadEquals"] = "equals",
                ["Exclaim"] = "!", ["DoubleQuote"] = "\"", ["Hash"] = "#", ["Dollar"] = "$", ["Percent"] = "%", ["Ampersand"] = "&", ["Quote"] = "'", ["LeftParen"] = "(",
                ["RightParen"] = ")", ["Asterisk"] = "*", ["Plus"] = "+", ["Comma"] = ",", ["Minus"] = "-", ["Period"] = ".", ["Slash"] = "/", ["Colon"] = ":", ["Semicolon"] = ";",
                ["Less"] = "<", ["Equals"] = "=", ["Greater"] = ">", ["Question"] = "?", ["At"] = "@", ["LeftBracket"] = "[", ["Backslash"] = "\\", ["RightBracket"] = "]",
                ["Caret"] = "^", ["Underscore"] = "_", ["BackQuote"] = "`", ["LeftCurlyBracket"] = "{", ["Pipe"] = "|", ["RightCurlyBracket"] = "}", ["Tilde"] = "~",
            };
            foreach (var n in Enum.GetNames(typeof(KeyCode)))
            {
                var k = (KeyCode)Enum.Parse(typeof(KeyCode), n);
                string un;
                if (special.TryGetValue(n, out var sp)) un = sp;
                else if (n.StartsWith("Alpha") && n.Length == 6) un = n.Substring(5);
                else if (n.StartsWith("Keypad") && n.Length == 7 && char.IsDigit(n[6])) un = "[" + n[6] + "]";
                else if (n.StartsWith("Mouse")) un = "mouse " + n.Substring(5);
                else if (n.StartsWith("JoystickButton")) un = "joystick button " + n.Substring(14);
                else if (n.StartsWith("Joystick") && n.Contains("Button")) un = "joystick " + n.Substring(8, n.IndexOf("Button") - 8) + " button " + n.Substring(n.IndexOf("Button") + 6);
                else un = n.ToLowerInvariant();
                Add(k, un);
                names.TryAdd(n, k);   // KeyCode enum names are accepted in scripted input
            }
        }

        public static KeyCode? KeyOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            BuildNames();
            if (names.TryGetValue(name, out var k)) return k;
            if (names.TryGetValue(name.Replace(" ", ""), out k)) return k;
            return null;
        }
        public static string UnityName(KeyCode k) { BuildNames(); return unityNames.TryGetValue(k, out var n) ? n : k.ToString().ToLowerInvariant(); }

        public static IEnumerable<KeyCode> PositiveButtons(string virtualName)
        {
            foreach (var a in Axes) if (a.Name.Equals(virtualName, StringComparison.OrdinalIgnoreCase) && a.Type == 0)
                { if (a.PosK is KeyCode p) yield return p; else if (a.AltPosK is KeyCode ap) yield return ap; break; }
        }

        // ------------------------------------------------------------------ per-frame update (called at the start of each frame)
        public static void BeginFrame(int frame, float dt)
        {
            Load();
            if (Cur.Number == frame) return;
            Prev = Cur; Cur = new Frame { Number = frame };
            Play.Input.Fill(Cur, frame);
            // touches emulate the mouse (Input.simulateMouseWithTouches)
            if (UnityEngine.Input.simulateMouseWithTouches && Cur.Touches.TryGetValue(0, out var t0))
            {
                Cur.Mouse = new Vector3(t0.pos.x, t0.pos.y, 0);
                if (!t0.ended) Cur.Keys.Add(KeyCode.Mouse0);
            }
            reset = false;
            foreach (var a in axes) Step(a, dt);
        }

        static bool Held(KeyCode? k) => k is KeyCode kk && Held(kk);
        public static bool Held(KeyCode k)
        {
            if (reset) return false;
            if (Cur.Keys.Contains(k)) return true;
            // "joystick button n" is held when that button is held on any joystick
            if (k >= KeyCode.JoystickButton0 && k <= KeyCode.JoystickButton19)
            {
                int b = k - KeyCode.JoystickButton0;
                for (int j = 1; j <= 8; j++) if (Cur.Keys.Contains(KeyCode.Joystick1Button0 + (j - 1) * 20 + b)) return true;
            }
            return false;
        }
        static bool PrevHeld(KeyCode k) => Prev.Keys.Contains(k);
        public static bool Down(KeyCode k) => Held(k) && !PrevHeld(k);
        public static bool Up(KeyCode k) => !reset && !Cur.Keys.Contains(k) && PrevHeld(k);

        static float Target(Axis a) => (Held(a.PosK) || Held(a.AltPosK) ? 1 : 0) - (Held(a.NegK) || Held(a.AltNegK) ? 1 : 0);

        static void Step(Axis a, float dt)
        {
            switch (a.Type)
            {
                case 0:
                {
                    float target = Target(a);
                    if (target != 0)
                    {
                        if (a.Snap && a.Value != 0 && Math.Sign(a.Value) != Math.Sign(target)) a.Value = 0;
                        a.Value = Mathf.MoveTowards(a.Value, target, a.Sensitivity * dt);
                    }
                    else a.Value = Mathf.MoveTowards(a.Value, 0, a.Gravity * dt);
                    break;
                }
                case 1:
                {
                    var d = a.AxisIndex switch { 0 => Cur.Mouse.x - Prev.Mouse.x, 1 => Cur.Mouse.y - Prev.Mouse.y, _ => Cur.Scroll.y };
                    if (Prev.Number < 0 && a.AxisIndex < 2) d = 0;
                    a.Value = d * a.Sensitivity;
                    break;
                }
                default:
                {
                    float v = 0;
                    foreach (var ((joy, axis), val) in Cur.JoyAxes) if (axis == a.AxisIndex && (a.JoyNum == 0 || joy == 0 || joy == a.JoyNum)) v = Math.Abs(val) > Math.Abs(v) ? val : v;
                    a.Value = Math.Abs(v) < a.Dead ? 0 : v;
                    break;
                }
            }
        }

        static IEnumerable<Axis> Named(string name, string kind)
        {
            var list = Axes.Where(a => a.Name == name).ToList();
            if (list.Count == 0 && !Cur.AxisOverride.ContainsKey(name)) throw new ArgumentException($"Input {kind} {name} is not setup.\n To change the input settings use: Edit -> Settings -> Input");
            return list;
        }

        public static float GetAxis(string name, bool raw)
        {
            var list = Named(name, "Axis");
            if (Cur.AxisOverride.TryGetValue(name, out var o)) return o;
            if (reset) return 0;
            float best = 0;
            foreach (var a in list)
            {
                float v = a.Type == 0 && raw ? Target(a) : a.Value;
                if (a.Invert) v = -v;
                if (Math.Abs(v) > Math.Abs(best)) best = v;
            }
            return best;
        }

        public static bool Button(string name, int mode)
        {
            var list = Named(name, "Button");
            if (Cur.AxisOverride.TryGetValue(name, out var o) && mode == 0) return o != 0;
            foreach (var a in list)
            {
                if (a.Type != 0) continue;
                foreach (var k in new[] { a.PosK, a.NegK, a.AltPosK, a.AltNegK })
                {
                    if (k is not KeyCode kk) continue;
                    if (mode == 0 ? Held(kk) : mode == 1 ? Down(kk) : Up(kk)) return true;
                }
            }
            return false;
        }

        public static KeyCode KeyByName(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            BuildNames();
            if (unityNames.ContainsValue(name.ToLowerInvariant()) && names.TryGetValue(name, out var k)) return k;
            throw new ArgumentException($"Input Key named: {name} is unknown");
        }

        public static void ResetAxes() { foreach (var a in Axes) a.Value = 0; reset = true; }

        // ------------------------------------------------------------------ touches
        public static Touch[] Touches(float dt)
        {
            var res = new List<Touch>();
            foreach (var (finger, (pos, ended)) in Cur.Touches.OrderBy(t => t.Key))
            {
                bool had = Prev.Touches.TryGetValue(finger, out var prev) && !prev.ended;
                var phase = ended ? TouchPhase.Ended : !had ? TouchPhase.Began : (pos - prev.pos).sqrMagnitude > 1e-6f ? TouchPhase.Moved : TouchPhase.Stationary;
                if (ended && !had) continue;
                res.Add(new Touch
                {
                    m_FingerId = finger, m_Position = pos, m_RawPosition = pos, m_PositionDelta = had ? pos - prev.pos : Vector2.zero,
                    m_TimeDelta = had ? dt : 0, m_TapCount = 1, m_Phase = phase, m_Pressure = 1, m_maximumPossiblePressure = 1, m_Type = TouchType.Direct, m_Radius = 1,
                });
            }
            return res.ToArray();
        }
    }
}

namespace UnityEngine
{
    public partial struct Touch
    {
        internal int m_FingerId, m_TapCount; internal Vector2 m_Position, m_RawPosition, m_PositionDelta; internal float m_TimeDelta, m_Pressure, m_maximumPossiblePressure, m_AltitudeAngle, m_AzimuthAngle, m_Radius, m_RadiusVariance;
        internal TouchPhase m_Phase; internal TouchType m_Type;
        public int fingerId { get => m_FingerId; set => m_FingerId = value; }
        public Vector2 position { get => m_Position; set => m_Position = value; }
        public Vector2 rawPosition { get => m_RawPosition; set => m_RawPosition = value; }
        public Vector2 deltaPosition { get => m_PositionDelta; set => m_PositionDelta = value; }
        public float deltaTime { get => m_TimeDelta; set => m_TimeDelta = value; }
        public int tapCount { get => m_TapCount; set => m_TapCount = value; }
        public TouchPhase phase { get => m_Phase; set => m_Phase = value; }
        public float pressure { get => m_Pressure; set => m_Pressure = value; }
        public float maximumPossiblePressure { get => m_maximumPossiblePressure; set => m_maximumPossiblePressure = value; }
        public TouchType type { get => m_Type; set => m_Type = value; }
        public float altitudeAngle { get => m_AltitudeAngle; set => m_AltitudeAngle = value; }
        public float azimuthAngle { get => m_AzimuthAngle; set => m_AzimuthAngle = value; }
        public float radius { get => m_Radius; set => m_Radius = value; }
        public float radiusVariance { get => m_RadiusVariance; set => m_RadiusVariance = value; }
    }

    public partial class Input
    {
        static bool s_SimulateMouse = true, s_MultiTouch = true;
        static MiniUnity.InputSim.Frame F => MiniUnity.InputSim.Cur;
        public static float GetAxis(string axisName) => MiniUnity.InputSim.GetAxis(axisName, false);
        public static float GetAxisRaw(string axisName) => MiniUnity.InputSim.GetAxis(axisName, true);
        public static bool GetButton(string buttonName) => MiniUnity.InputSim.Button(buttonName, 0);
        public static bool GetButtonDown(string buttonName) => MiniUnity.InputSim.Button(buttonName, 1);
        public static bool GetButtonUp(string buttonName) => MiniUnity.InputSim.Button(buttonName, 2);
        public static bool GetMouseButton(int button) => MiniUnity.InputSim.Held(KeyCode.Mouse0 + button);
        public static bool GetMouseButtonDown(int button) => MiniUnity.InputSim.Down(KeyCode.Mouse0 + button);
        public static bool GetMouseButtonUp(int button) => MiniUnity.InputSim.Up(KeyCode.Mouse0 + button);
        public static void ResetInputAxes() => MiniUnity.InputSim.ResetAxes();
        public static bool IsJoystickPreconfigured(string joystickName) => false;
        public static string[] GetJoystickNames() => F.JoyAxes.Count > 0 ? new[] { "MiniUnity Scripted Joystick" } : Array.Empty<string>();
        public static Touch GetTouch(int index) { var t = touches; if (index < 0 || index >= t.Length) throw new ArgumentException("Index out of bounds."); return t[index]; }
        public static bool GetKey(KeyCode key) => MiniUnity.InputSim.Held(key);
        public static bool GetKey(string name) => MiniUnity.InputSim.Held(MiniUnity.InputSim.KeyByName(name));
        public static bool GetKeyUp(KeyCode key) => MiniUnity.InputSim.Up(key);
        public static bool GetKeyUp(string name) => MiniUnity.InputSim.Up(MiniUnity.InputSim.KeyByName(name));
        public static bool GetKeyDown(KeyCode key) => MiniUnity.InputSim.Down(key);
        public static bool GetKeyDown(string name) => MiniUnity.InputSim.Down(MiniUnity.InputSim.KeyByName(name));
        public static bool anyKey => F.Keys.Count > 0;
        public static bool anyKeyDown => F.Keys.Any(k => !MiniUnity.InputSim.Prev.Keys.Contains(k));
        public static string inputString => F.Text;
        public static Vector3 mousePosition => F.Mouse;
        public static Vector2 mouseScrollDelta => F.Scroll;
        public static bool mousePresent => true;
        public static int touchCount => touches.Length;
        public static Touch[] touches => MiniUnity.InputSim.Touches(Time.unscaledDeltaTime);
        public static bool touchSupported => true;
        public static bool multiTouchEnabled { get => s_MultiTouch; set => s_MultiTouch = value; }
        public static bool simulateMouseWithTouches { get => s_SimulateMouse; set => s_SimulateMouse = value; }
        public static Vector3 acceleration => new(0, 0, -1);
        public static int accelerationEventCount => 0;
        public static AccelerationEvent[] accelerationEvents => Array.Empty<AccelerationEvent>();
        public static DeviceOrientation deviceOrientation => DeviceOrientation.Unknown;
        public static bool isGyroAvailable => false;
        public static bool backButtonLeavesApp { get => false; set { } }
        public static IMECompositionMode imeCompositionMode { get => IMECompositionMode.Auto; set { } }
        public static string compositionString => "";
        public static bool imeIsSelected => false;
        public static Vector2 compositionCursorPos { get => Vector2.zero; set { } }
        public static bool eatKeyPressOnTextFieldFocus { get => false; set { } }
        public static bool compensateSensors { get => true; set { } }
        public static bool touchPressureSupported => false;
        public static bool stylusTouchSupported => false;
        public static int penEventCount => 0;
    }
}
