using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MiniUnity;
using UnityEngine;

namespace UnityEngineInternal.Input
{
    // Native side of the Input System package (com.unity.inputsystem): the package's NativeInputRuntime talks to these
    // statics. MiniUnity reports a keyboard, a mouse and a touchscreen and turns the scripted input of `ua play --input` /
    // `ua test --input` (MiniUnity.InputSim) into state events, delivered through onUpdate like Unity's native backend.

    internal unsafe delegate void NativeUpdateCallback(NativeInputUpdateType updateType, NativeInputEventBuffer* buffer);

    internal enum NativeInputEventType
    {
        DeviceRemoved = 1146242381,
        DeviceConfigChanged = 1145259591,
        Text = 1413830740,
        State = 1398030676,
        Delta = 1145852993,
    }

    [Flags]
    internal enum NativeInputUpdateType
    {
        Dynamic = 1,
        Fixed = 2,
        BeforeRender = 4,
        Editor = 8,
        IgnoreFocus = unchecked((int)0x80000000),
    }

    [StructLayout(LayoutKind.Explicit, Size = structSize)]
    internal unsafe struct NativeInputEventBuffer
    {
        public const int structSize = 20;
        [FieldOffset(0)] public void* eventBuffer;
        [FieldOffset(8)] public int eventCount;
        [FieldOffset(12)] public int sizeInBytes;
        [FieldOffset(16)] public int capacityInBytes;
    }

    [StructLayout(LayoutKind.Explicit, Size = structSize)]
    internal struct NativeInputEvent
    {
        public const int structSize = 20;
        [FieldOffset(0)] public NativeInputEventType type;
        [FieldOffset(4)] public ushort sizeInBytes;
        [FieldOffset(6)] public ushort deviceId;
        [FieldOffset(8)] public int eventId;
        [FieldOffset(12)] public double time;

        public NativeInputEvent(NativeInputEventType type, int sizeInBytes, int deviceId, double time)
        {
            this.type = type;
            this.sizeInBytes = (ushort)sizeInBytes;
            this.deviceId = (ushort)deviceId;
            eventId = 0;
            this.time = time;
        }
    }

    internal static unsafe partial class NativeInputSystem
    {
        public static NativeUpdateCallback onUpdate;
        public static Action<NativeInputUpdateType> onBeforeUpdate;
        public static Func<NativeInputUpdateType, bool> onShouldRunUpdate;

        public const int KeyboardId = 1, MouseId = 2, TouchscreenId = 3, GamepadId = 4, JoystickId = 5;
        const string GamepadJson = "{\"interface\":\"MiniUnity\",\"type\":\"Gamepad\",\"product\":\"Gamepad\"}";
        const string JoystickJson = "{\"interface\":\"MiniUnity\",\"type\":\"Joystick\",\"product\":\"Joystick\"}";
        // a gamepad / joystick is plugged in once a scripted input uses it (and stays, like a device left connected)
        static bool padConnected, joyConnected;
        static byte[] lastPad, lastJoy;
        static readonly (int id, string json)[] devices =
        {
            (KeyboardId, "{\"interface\":\"MiniUnity\",\"type\":\"Keyboard\",\"product\":\"Keyboard\"}"),
            (MouseId, "{\"interface\":\"MiniUnity\",\"type\":\"Mouse\",\"product\":\"Mouse\"}"),
            (TouchscreenId, "{\"interface\":\"MiniUnity\",\"type\":\"Touchscreen\",\"product\":\"Touchscreen\"}"),
        };

        static Action<int, string> s_DeviceDiscovered;
        static bool devicesPending;   // connected devices are pushed on the next update after the callback is installed (as natively)
        static readonly List<byte[]> queue = new();
        static int nextDeviceId = 1000, nextEventId = 1;
        static NativeInputUpdateType updateMask = NativeInputUpdateType.Dynamic | NativeInputUpdateType.Fixed | NativeInputUpdateType.BeforeRender | NativeInputUpdateType.Editor;

        public static Action<int, string> onDeviceDiscovered
        {
            get => s_DeviceDiscovered;
            set
            {
                if (s_DeviceDiscovered == null && value != null) devicesPending = true;
                s_DeviceDiscovered = value;
            }
        }

        internal static bool hasDeviceDiscoveredCallback { set { } }
        internal static bool allowInputDeviceCreationFromEvents { get; set; }

        public static double currentTime => Play.UnscaledTime;
        public static double currentTimeOffsetToRealtimeSinceStartup => 0;

        public static int AllocateDeviceId() => nextDeviceId++;
        public static void SetPollingFrequency(float hertz) { }
        public static void SetUpdateMask(NativeInputUpdateType mask) => updateMask = mask;
        internal static ulong GetBackgroundEventBufferSize() => 0;

        /// <summary>Device commands (IOCTL) are not supported by the simulated devices: -1, as for an unknown command in Unity.</summary>
        public static long IOCTL(int deviceId, int code, IntPtr data, int sizeInBytes) => -1;

        public static void QueueInputEvent(IntPtr inputEvent)
        {
            var e = (NativeInputEvent*)inputEvent;
            var bytes = new byte[Align(e->sizeInBytes)];
            Marshal.Copy(inputEvent, bytes, 0, e->sizeInBytes);
            fixed (byte* p = bytes)
            {
                var q = (NativeInputEvent*)p;
                q->eventId = (q->eventId & unchecked((int)0x80000000)) | nextEventId++;
            }
            queue.Add(bytes);
        }

        public static void QueueInputEvent<TInputEvent>(ref TInputEvent inputEvent) where TInputEvent : struct
        {
            var size = Marshal.SizeOf<TInputEvent>();
            var mem = Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(inputEvent, mem, false); QueueInputEvent(mem); }
            finally { Marshal.FreeHGlobal(mem); }
        }

        /// <summary>InputSystem.Update(): run one update of the given type now.</summary>
        public static void Update(NativeInputUpdateType updateType) => Run(updateType);

        internal static void NotifyBeforeUpdate(NativeInputUpdateType updateType) => onBeforeUpdate?.Invoke(updateType);
        internal static void NotifyUpdate(NativeInputUpdateType updateType, IntPtr eventBuffer) => onUpdate?.Invoke(updateType, (NativeInputEventBuffer*)eventBuffer);
        internal static void NotifyDeviceDiscovered(int deviceId, string deviceDescriptor) => s_DeviceDiscovered?.Invoke(deviceId, deviceDescriptor);
        internal static void ShouldRunUpdate(NativeInputUpdateType updateType, out bool retval) => retval = onShouldRunUpdate?.Invoke(updateType) ?? true;

        static readonly bool Debug = Environment.GetEnvironmentVariable("MINIUNITY_INPUT_DEBUG") == "1";

        static int Align(int n) => (n + 3) & ~3;

        // ------------------------------------------------------------------ player loop

        /// <summary>Called by the MiniUnity player loop: EarlyUpdate (Dynamic, after the frame's scripted input is read) and before each FixedUpdate (Fixed).</summary>
        internal static void Tick(NativeInputUpdateType updateType)
        {
            if (onUpdate == null)
                return;
            if (updateType == NativeInputUpdateType.Dynamic)
                Sample();
            if ((updateMask & updateType) == 0)
                return;
            ShouldRunUpdate(updateType, out bool run);
            if (Debug) Console.Error.WriteLine($"[input] tick {updateType} run={run} queued={queue.Count}");
            if (!run)
                return;
            Run(updateType);
        }

        static void Run(NativeInputUpdateType updateType)
        {
            if (devicesPending && s_DeviceDiscovered != null)
            {
                devicesPending = false;
                foreach (var (id, json) in devices)
                    s_DeviceDiscovered(id, json);
                padConnected = joyConnected = false;
            }
            if (s_DeviceDiscovered != null && InputSim.PadUsed && !padConnected) { padConnected = true; lastPad = null; s_DeviceDiscovered(GamepadId, GamepadJson); }
            if (s_DeviceDiscovered != null && InputSim.JoystickUsed && !joyConnected) { joyConnected = true; lastJoy = null; s_DeviceDiscovered(JoystickId, JoystickJson); }
            NotifyBeforeUpdate(updateType);
            if (onUpdate == null)
                return;
            int size = 0;
            foreach (var e in queue) size += e.Length;
            int capacity = Math.Max(size, 16);
            var mem = (byte*)Marshal.AllocHGlobal(capacity);
            try
            {
                int at = 0;
                foreach (var e in queue) { Marshal.Copy(e, 0, (IntPtr)(mem + at), e.Length); at += e.Length; }
                int count = queue.Count;
                queue.Clear();
                var buffer = new NativeInputEventBuffer { eventBuffer = mem, eventCount = count, sizeInBytes = size, capacityInBytes = capacity };
                onUpdate(updateType, &buffer);
                // events the package left in the buffer (e.g. for a later fixed update) stay queued, ahead of new ones
                if (buffer.eventCount > 0 && buffer.eventBuffer != null)
                {
                    var kept = new List<byte[]>();
                    var p = (byte*)buffer.eventBuffer;
                    for (int i = 0, off = 0; i < buffer.eventCount && off < buffer.sizeInBytes; i++)
                    {
                        int len = Align(((NativeInputEvent*)(p + off))->sizeInBytes);
                        var b = new byte[len];
                        Marshal.Copy((IntPtr)(p + off), b, 0, len);
                        kept.Add(b);
                        off += len;
                    }
                    queue.InsertRange(0, kept);
                }
            }
            finally { Marshal.FreeHGlobal((IntPtr)mem); }
        }

        /// <summary>New play run: forget what was sent (the package keeps its runtime and devices, like a domain that is not reloaded).</summary>
        internal static void Reset()
        {
            queue.Clear();
            lastKeys = null; lastMouse = default; mouseSent = false; lastTouches.Clear(); lastPad = lastJoy = null;
        }

        static bool Same(byte[] a, byte[] b) => b != null && a.AsSpan().SequenceEqual(b);

        /// <summary>GamepadState 'GPAD': buttons (uint, GamepadButton bits), leftStick, rightStick, leftTrigger, rightTrigger = 28 bytes.</summary>
        static void SampleGamepad(InputSim.Frame cur)
        {
            uint buttons = 0;
            Vector2 V(string n) => cur.Pad.TryGetValue(n, out var v) ? v : Vector2.zero;
            foreach (var (name, bit) in InputSim.PadButtonBits) if (cur.Pad.ContainsKey(name)) buttons |= 1u << bit;
            var dpad = V("dpad");
            if (dpad.y > 0.5f) buttons |= 1u; if (dpad.y < -0.5f) buttons |= 2u; if (dpad.x < -0.5f) buttons |= 4u; if (dpad.x > 0.5f) buttons |= 8u;
            var state = new byte[28];
            fixed (byte* p = state)
            {
                *(uint*)p = buttons; *(Vector2*)(p + 4) = V("leftstick"); *(Vector2*)(p + 12) = V("rightstick");
                *(float*)(p + 20) = V("lefttrigger").x; *(float*)(p + 24) = V("righttrigger").x;
            }
            if (Same(state, lastPad)) return;
            QueueState(GamepadId, "GPAD", state);
            lastPad = state;
        }

        /// <summary>JoystickState 'JOY ': buttons (int: hat up/down/left/right bits 0-3, trigger bit 4), stick = 12 bytes.</summary>
        static void SampleJoystick(InputSim.Frame cur)
        {
            int buttons = 0;
            Vector2 V(string n) => cur.Joy.TryGetValue(n, out var v) ? v : Vector2.zero;
            var hat = V("hat");
            if (hat.y > 0.5f) buttons |= 1; if (hat.y < -0.5f) buttons |= 2; if (hat.x < -0.5f) buttons |= 4; if (hat.x > 0.5f) buttons |= 8;
            if (cur.Joy.ContainsKey("trigger")) buttons |= 16;
            var state = new byte[12];
            fixed (byte* p = state) { *(int*)p = buttons; *(Vector2*)(p + 4) = V("stick"); }
            if (Same(state, lastJoy)) return;
            QueueState(JoystickId, "JOY ", state);
            lastJoy = state;
        }

        // ------------------------------------------------------------------ scripted input → state events

        static byte[] lastKeys;
        static (Vector2 pos, Vector2 scroll, ushort buttons) lastMouse;
        static bool mouseSent;
        static readonly Dictionary<int, (Vector2 pos, Vector2 start, double startTime)> lastTouches = new();

        static int FourCC(string s) => (s[0] << 24) | (s[1] << 16) | (s[2] << 8) | s[3];

        static void QueueState(int deviceId, string format, byte[] state)
        {
            int size = NativeInputEvent.structSize + 4 + state.Length;
            var bytes = new byte[Align(size)];
            fixed (byte* p = bytes)
            {
                *(NativeInputEvent*)p = new NativeInputEvent(NativeInputEventType.State, size, deviceId, currentTime);
                *(int*)(p + NativeInputEvent.structSize) = FourCC(format);
            }
            Buffer.BlockCopy(state, 0, bytes, NativeInputEvent.structSize + 4, state.Length);
            fixed (byte* p = bytes)
                QueueInputEvent((IntPtr)p);
        }

        static void Sample()
        {
            var cur = InputSim.Cur;
            if (padConnected) SampleGamepad(cur);
            if (joyConnected) SampleJoystick(cur);

            // keyboard: one bit per Key (KeyboardState 'KEYS', 14 bytes)
            var keys = new byte[KeyboardStateBytes];
            foreach (var k in cur.Keys)
                if (KeyBit(k) is int bit)
                    keys[bit >> 3] |= (byte)(1 << (bit & 7));
            if (lastKeys == null || !keys.AsSpan().SequenceEqual(lastKeys))
            {
                QueueState(KeyboardId, "KEYS", keys);
                lastKeys = keys;
            }

            // mouse: MouseState 'MOUS' (position, delta, scroll, buttons, displayIndex, clickCount) = 30 bytes
            var pos = new Vector2(cur.Mouse.x, cur.Mouse.y);
            ushort buttons = (ushort)((cur.Keys.Contains(KeyCode.Mouse0) ? 1 : 0) | (cur.Keys.Contains(KeyCode.Mouse1) ? 2 : 0) | (cur.Keys.Contains(KeyCode.Mouse2) ? 4 : 0)
                                      | (cur.Keys.Contains(KeyCode.Mouse3) ? 8 : 0) | (cur.Keys.Contains(KeyCode.Mouse4) ? 16 : 0));
            if (!mouseSent || pos != lastMouse.pos || cur.Scroll != lastMouse.scroll || buttons != lastMouse.buttons)
            {
                var m = new byte[30];
                fixed (byte* p = m)
                {
                    *(Vector2*)p = pos;
                    *(Vector2*)(p + 8) = mouseSent ? pos - lastMouse.pos : Vector2.zero;
                    *(Vector2*)(p + 16) = cur.Scroll;
                    *(ushort*)(p + 24) = buttons;
                }
                QueueState(MouseId, "MOUS", m);
                lastMouse = (pos, cur.Scroll, buttons);
                mouseSent = true;
            }

            // touches: one TouchState 'TOUC' (56 bytes) per changed touch; touchId 0 means "no touch" in the package
            foreach (var (id, (tpos, ended)) in cur.Touches)
            {
                bool had = lastTouches.TryGetValue(id, out var last);
                byte phase = ended ? (byte)3 : !had ? (byte)1 : tpos != last.pos ? (byte)2 : (byte)5;
                if (had && phase == 5)
                    continue;
                var start = had ? last.start : tpos;
                double startTime = had ? last.startTime : currentTime;
                var t = new byte[56];
                fixed (byte* p = t)
                {
                    *(int*)p = id + 1;
                    *(Vector2*)(p + 4) = tpos;
                    *(Vector2*)(p + 12) = had ? tpos - last.pos : Vector2.zero;
                    *(float*)(p + 20) = 1f;
                    p[32] = phase;
                    *(double*)(p + 40) = startTime;
                    *(Vector2*)(p + 48) = start;
                }
                QueueState(TouchscreenId, "TOUC", t);
                if (ended) lastTouches.Remove(id);
                else lastTouches[id] = (tpos, start, startTime);
            }
            foreach (var id in new List<int>(lastTouches.Keys))
            {
                if (cur.Touches.ContainsKey(id))
                    continue;
                var last = lastTouches[id];
                var t = new byte[56];
                fixed (byte* p = t)
                {
                    *(int*)p = id + 1;
                    *(Vector2*)(p + 4) = last.pos;
                    p[32] = 3;   // Ended
                    *(double*)(p + 40) = last.startTime;
                    *(Vector2*)(p + 48) = last.start;
                }
                QueueState(TouchscreenId, "TOUC", t);
                lastTouches.Remove(id);
            }
        }

        // UnityEngine.InputSystem.Key order (bit index in KeyboardState); KeyCount = OEM5 = 110 → 14 bytes
        const int KeyboardStateBytes = 14;
        static readonly string[] KeyOrder =
        {
            "None", "Space", "Enter", "Tab", "Backquote", "Quote", "Semicolon", "Comma", "Period", "Slash", "Backslash", "LeftBracket", "RightBracket",
            "Minus", "Equals", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X",
            "Y", "Z", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0", "LeftShift", "RightShift",
            "LeftAlt", "RightAlt", "LeftCtrl", "RightCtrl", "LeftMeta", "RightMeta", "ContextMenu", "Escape", "LeftArrow", "RightArrow", "UpArrow",
            "DownArrow", "Backspace", "PageDown", "PageUp", "Home", "End", "Insert", "Delete", "CapsLock", "NumLock", "PrintScreen", "ScrollLock",
            "Pause", "NumpadEnter", "NumpadDivide", "NumpadMultiply", "NumpadPlus", "NumpadMinus", "NumpadPeriod", "NumpadEquals", "Numpad0",
            "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9", "F1", "F2", "F3", "F4", "F5", "F6",
            "F7", "F8", "F9", "F10", "F11", "F12", "OEM1", "OEM2", "OEM3", "OEM4", "OEM5",
        };
        static Dictionary<KeyCode, int> keyBits;

        /// <summary>Bit of a legacy KeyCode in the Input System keyboard state, if the keyboard has that key.</summary>
        internal static int? KeyBit(KeyCode k)
        {
            if (keyBits == null)
            {
                var index = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < KeyOrder.Length; i++) index[KeyOrder[i]] = i;
                keyBits = new Dictionary<KeyCode, int>();
                void Map(KeyCode code, string key) => keyBits[code] = index[key];
                Map(KeyCode.Space, "Space"); Map(KeyCode.Return, "Enter"); Map(KeyCode.Tab, "Tab"); Map(KeyCode.BackQuote, "Backquote");
                Map(KeyCode.Quote, "Quote"); Map(KeyCode.Semicolon, "Semicolon"); Map(KeyCode.Comma, "Comma"); Map(KeyCode.Period, "Period");
                Map(KeyCode.Slash, "Slash"); Map(KeyCode.Backslash, "Backslash"); Map(KeyCode.LeftBracket, "LeftBracket"); Map(KeyCode.RightBracket, "RightBracket");
                Map(KeyCode.Minus, "Minus"); Map(KeyCode.Equals, "Equals");
                for (int i = 0; i < 26; i++) Map(KeyCode.A + i, ((char)('A' + i)).ToString());
                for (int i = 0; i <= 9; i++) Map(KeyCode.Alpha0 + i, "Digit" + i);
                Map(KeyCode.LeftShift, "LeftShift"); Map(KeyCode.RightShift, "RightShift"); Map(KeyCode.LeftAlt, "LeftAlt"); Map(KeyCode.RightAlt, "RightAlt");
                Map(KeyCode.AltGr, "RightAlt"); Map(KeyCode.LeftControl, "LeftCtrl"); Map(KeyCode.RightControl, "RightCtrl");
                Map(KeyCode.LeftCommand, "LeftMeta"); Map(KeyCode.RightCommand, "RightMeta"); Map(KeyCode.Menu, "ContextMenu"); Map(KeyCode.Escape, "Escape");
                Map(KeyCode.LeftArrow, "LeftArrow"); Map(KeyCode.RightArrow, "RightArrow"); Map(KeyCode.UpArrow, "UpArrow"); Map(KeyCode.DownArrow, "DownArrow");
                Map(KeyCode.Backspace, "Backspace"); Map(KeyCode.PageDown, "PageDown"); Map(KeyCode.PageUp, "PageUp"); Map(KeyCode.Home, "Home");
                Map(KeyCode.End, "End"); Map(KeyCode.Insert, "Insert"); Map(KeyCode.Delete, "Delete"); Map(KeyCode.CapsLock, "CapsLock");
                Map(KeyCode.Numlock, "NumLock"); Map(KeyCode.Print, "PrintScreen"); Map(KeyCode.ScrollLock, "ScrollLock"); Map(KeyCode.Pause, "Pause");
                Map(KeyCode.KeypadEnter, "NumpadEnter"); Map(KeyCode.KeypadDivide, "NumpadDivide"); Map(KeyCode.KeypadMultiply, "NumpadMultiply");
                Map(KeyCode.KeypadPlus, "NumpadPlus"); Map(KeyCode.KeypadMinus, "NumpadMinus"); Map(KeyCode.KeypadPeriod, "NumpadPeriod");
                Map(KeyCode.KeypadEquals, "NumpadEquals");
                for (int i = 0; i <= 9; i++) Map(KeyCode.Keypad0 + i, "Numpad" + i);
                for (int i = 0; i < 12; i++) Map(KeyCode.F1 + i, "F" + (i + 1));
            }
            return keyBits.TryGetValue(k, out var b) ? b : null;
        }
    }
}
