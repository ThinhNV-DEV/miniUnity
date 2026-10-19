using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace VideoRecreate
{
    /// <summary>
    /// Mô phỏng TẤT ĐỊNH chuyển động theo bước vật lý (thêm 2026-10-10, video 12 Jitter): vị trí là hàm thuần của director.time.
    /// - Stepped: vị trí chỉ đổi ở mỗi bước fixedStep (như Rigidbody không interpolate).
    /// - Interpolated: vẽ giữa hai bước gần nhất (chậm 1 bước – như RigidbodyInterpolation.Interpolate).
    /// - Extrapolated: bước gần nhất + vận tốc × thời gian đã trôi (không trễ; gặp `wall` thì vượt qua rồi giật ngược).
    /// - Smooth: liên tục (tham chiếu).
    /// Mỗi segment bắt đầu lại từ origin (đặt ở các điểm cắt cảnh). slowmo &lt; 1 làm chậm cả thời gian mô phỏng (slow motion của VIDEO);
    /// timeScale &lt; 1 là Time.timeScale của GAME: bước vật lý thưa ra nhưng khung hình vẫn 1/frameRate giây thật.
    /// cameraTarget: mục tiêu cho virtual camera – mượt (mỗi khung), khoá vào bóng (LateUpdate) hoặc `cameraFixed` (camera đi theo bước).
    /// ghosts: bóng mờ tại các bước vật lý gần nhất; frameMarks: vạch tại vị trí bóng mà từng khung hình (frameRate) nhìn thấy –
    /// khung trùng vị trí xếp chồng lên trên → thấy rõ "khung đứng hình". hitches: thời điểm (giây mô phỏng) có một khung hình dài
    /// hitchLength → khung đó chứa 2 bước. jumps: thời điểm bấm nhảy; CheckInFixedUpdate nhảy theo số bước trong khung có lần bấm
    /// (0 bước = mất, 2 bước = nhảy đôi, cao gấp đôi), Buffered luôn nhảy đúng một lần ở bước kế tiếp. hurdles: rào (con "Bar" rung khi bị va).
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-200)]
    public class PhysicsStepSim : MonoBehaviour
    {
        public enum Mode { Smooth, Stepped, Interpolated, Extrapolated }
        public enum JumpMode { CheckInFixedUpdate, Buffered }

        [Serializable]
        public class Segment
        {
            public float start;
            public Mode mode = Mode.Stepped;
            public float speed = 8f;
            [Tooltip("Hệ số thời gian mô phỏng (0.1 = chậm 10 lần) – slow motion của video.")] public float slowmo = 1f;
            [Tooltip("Time.timeScale của game: bước vật lý thưa ra, khung hình giữ nguyên.")] public float timeScale = 1f;
            [Tooltip("Camera khoá vào vị trí bóng (thay vì đi mượt).")] public bool cameraLocked;
            [Tooltip("Camera cập nhật theo bước vật lý (CinemachineBrain · Fixed Update).")] public bool cameraFixed;
            public bool ghosts, frames;
            public Vector3 origin;
            [Tooltip("> 0: quãng đường bóng dừng lại (tường).")] public float wall;
            [Tooltip("Giây mô phỏng có khung hình dài hitchLength.")] public float[] hitches = Array.Empty<float>();
            [Tooltip("Giây mô phỏng bấm nhảy.")] public float[] jumps = Array.Empty<float>();
            public JumpMode jumpMode = JumpMode.Buffered;
        }

        public PlayableDirector director;
        public Transform ball, cameraTarget;
        public float radius = 0.5f;
        public float fixedStep = 0.02f;
        [Tooltip("Tốc độ khung hình giả lập cho frameMarks.")] public float frameRate = 60f;
        public float hitchLength = 0.04f;
        [Tooltip("Độ cao một lần nhảy (m) và thời gian bay (giây mô phỏng).")] public float jumpHeight = 1.6f, jumpTime = 0.6f;
        public List<Segment> segments = new List<Segment>();
        public Transform[] ghosts = Array.Empty<Transform>();
        public Transform[] frameMarks = Array.Empty<Transform>();
        [Tooltip("Rào chắn dọc đường (khoảng cách tính từ origin theo direction = vị trí x của rào).")] public Transform[] hurdles = Array.Empty<Transform>();
        public float hurdleHeight = 0.9f;
        public Vector3 direction = Vector3.right;
        public float markHeight = 1.5f, markStack = 0.32f;

        static readonly List<PhysicsStepSim> all = new List<PhysicsStepSim>();
        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() => all.Remove(this);

        /// <summary>Gọi sau director.Evaluate() khi chụp bằng code (Edit mode không tự Update).</summary>
        public static void ApplyAll() { foreach (var s in all) if (s && s.isActiveAndEnabled) s.Apply(); }

        void Update() => Apply();

        Segment SegmentAt(double t)
        {
            Segment cur = null;
            foreach (var s in segments) if (s.start <= t && (cur == null || s.start >= cur.start)) cur = s;
            return cur ?? (segments.Count > 0 ? segments[0] : null);
        }

        // ---------------------------------------------------------------- khung hình (có hitch)
        int HitchesBefore(Segment s, int k)
        {
            int n = 0;
            foreach (var h in s.hitches) if (Mathf.RoundToInt(h * frameRate) < k) n++;
            return n;
        }

        /// <summary>Thời điểm (giây thật của mô phỏng) của khung hình k: 1/frameRate mỗi khung, khung sau hitch dài hitchLength.</summary>
        public double FrameTime(Segment s, int k) => k / (double)frameRate + HitchesBefore(s, k) * (hitchLength - 1.0 / frameRate);

        public int FrameAt(Segment s, double t)
        {
            int k = (int)Math.Floor(t * frameRate) + 1;
            while (k > 0 && FrameTime(s, k) > t + 1e-9) k--;
            return k;
        }

        // ---------------------------------------------------------------- bước vật lý
        int StepIndex(Segment s, double gameT) => (int)Math.Floor(gameT / fixedStep + 1e-6);

        /// <summary>Vị trí vật lý thật sau bước k (dừng ở wall).</summary>
        float PhysAt(Segment s, int k)
        {
            float d = s.speed * Mathf.Max(0, k) * fixedStep;
            return s.wall > 0 ? Mathf.Min(d, s.wall) : d;
        }

        /// <summary>Quãng đường hiển thị ở thời gian game gameT theo chế độ m.</summary>
        float Distance(Segment s, Mode m, double gameT)
        {
            int k = StepIndex(s, gameT);
            switch (m)
            {
                case Mode.Stepped: return PhysAt(s, k);
                case Mode.Interpolated:
                {
                    float a = (float)(gameT / fixedStep - k);
                    return Mathf.Lerp(PhysAt(s, k - 1), PhysAt(s, k), Mathf.Clamp01(a));
                }
                case Mode.Extrapolated:
                {
                    float p = PhysAt(s, k);
                    float v = PhysAt(s, k + 1) > p ? s.speed : 0;   // vận tốc sau bước k (0 khi đã chạm tường)
                    return p + v * (float)(gameT - k * fixedStep);
                }
                default:
                {
                    float d = s.speed * (float)gameT;
                    return s.wall > 0 ? Mathf.Min(d, s.wall) : d;
                }
            }
        }

        // ---------------------------------------------------------------- nhảy
        /// <summary>Danh sách (thời điểm game bắt đầu nhảy, hệ số độ cao).</summary>
        List<(double t, float mul)> JumpStarts(Segment s)
        {
            var res = new List<(double, float)>();
            foreach (var press in s.jumps)
            {
                if (s.jumpMode == JumpMode.Buffered)
                {
                    // Update đặt cờ ở khung có lần bấm; FixedUpdate kế tiếp nhảy rồi xoá cờ
                    int f = FrameAt(s, press);
                    if (FrameTime(s, f) < press - 1e-9) f++;
                    int k = StepIndex(s, FrameTime(s, f) * s.timeScale) + 1;
                    res.Add((k * fixedStep, 1f));
                }
                else
                {
                    // GetButtonDown đúng trong khung f (khung đầu tiên sau lần bấm); số bước chạy trong khung đó = số lần kiểm tra
                    int f = FrameAt(s, press);
                    if (FrameTime(s, f) < press - 1e-9) f++;
                    int k0 = StepIndex(s, FrameTime(s, f - 1) * s.timeScale), k1 = StepIndex(s, FrameTime(s, f) * s.timeScale);
                    int n = k1 - k0;
                    if (n > 0) res.Add(((k0 + 1) * fixedStep, n));
                }
            }
            return res;
        }

        float Height(List<(double t, float mul)> starts, double gameT)
        {
            (double t, float mul) cur = (double.NaN, 0);
            foreach (var j in starts) if (j.t <= gameT + 1e-9 && (double.IsNaN(cur.t) || j.t > cur.t)) cur = j;
            if (double.IsNaN(cur.t)) return 0;
            float u = (float)((gameT - cur.t) / (jumpTime * Mathf.Sqrt(cur.mul)));
            return u >= 1 ? 0 : 4 * jumpHeight * cur.mul * u * (1 - u);
        }

        /// <summary>Thời gian game "được hiển thị" ứng với chế độ: Stepped/Extrapolated lấy độ cao ở bước gần nhất, Interpolated chậm 1 bước.</summary>
        double HeightTime(Segment s, Mode m, double gameT)
        {
            int k = StepIndex(s, gameT);
            switch (m)
            {
                case Mode.Stepped: case Mode.Extrapolated: return k * fixedStep;
                case Mode.Interpolated: return Math.Max(0, gameT - fixedStep);
                default: return gameT;
            }
        }

        public void Apply()
        {
            if (director == null || ball == null) return;
            var s = SegmentAt(director.time);
            if (s == null) return;
            double simT = Math.Max(0, (director.time - s.start) * s.slowmo);
            double gameT = simT * s.timeScale;
            Vector3 dir = direction.normalized;
            var jumps = s.jumps.Length > 0 ? JumpStarts(s) : null;
            float H(double t) => jumps == null ? 0 : Height(jumps, t);

            float d = Distance(s, s.mode, gameT);
            float h = H(HeightTime(s, s.mode, gameT));
            ball.position = s.origin + dir * d + Vector3.up * h;
            ball.rotation = Quaternion.AngleAxis(-d / radius * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, dir) * -1f);
            if (cameraTarget != null)
            {
                float camD = s.cameraFixed ? Distance(s, Mode.Stepped, gameT)
                    : s.cameraLocked || s.mode == Mode.Interpolated ? d : Distance(s, Mode.Smooth, gameT);
                cameraTarget.position = s.origin + dir * camD;
            }

            // rào: thanh ngang rung khi bóng đi qua mà không đủ cao
            foreach (var hd in hurdles)
            {
                if (hd == null) continue;
                var bar = hd.Find("Bar");
                if (bar == null) continue;
                float hx = Vector3.Dot(hd.position - s.origin, dir);
                double hit = double.NaN;
                if (s.speed > 0 && hx > 0)
                {
                    double tHit = hx / s.speed;   // thời gian game bóng tới rào
                    if (tHit <= gameT && H(tHit) < hurdleHeight - radius * .5f) hit = tHit;
                }
                float shake = 0;
                if (!double.IsNaN(hit))
                {
                    float e = (float)((gameT - hit) * 1.0);
                    shake = 25f * Mathf.Exp(-e * 4f) * Mathf.Sin(e * 40f);
                }
                bar.localRotation = Quaternion.Euler(shake, 0, 0);
            }

            int step = StepIndex(s, gameT);
            for (int i = 0; i < ghosts.Length; i++)
            {
                if (ghosts[i] == null) continue;
                int k = step - i;
                bool on = s.ghosts && k >= 0;
                ghosts[i].gameObject.SetActive(on);
                if (on) ghosts[i].position = s.origin + dir * PhysAt(s, k) + Vector3.up * H(k * fixedStep);
            }

            int frame = FrameAt(s, simT);
            float lastD = float.NaN;
            int stack = 0;
            // vạch khung hình: từ cũ tới mới, khung trùng vị trí với khung trước thì chồng lên
            for (int i = frameMarks.Length - 1; i >= 0; i--)
            {
                var m = frameMarks[i];
                if (m == null) continue;
                int f = frame - i;
                bool on = s.frames && f >= 0;
                m.gameObject.SetActive(on);
                if (!on) continue;
                double ft = FrameTime(s, f) * s.timeScale;
                float fd = Distance(s, s.mode, ft);
                stack = Mathf.Approximately(fd, lastD) ? stack + 1 : 0;
                lastD = fd;
                m.position = s.origin + dir * fd + Vector3.up * (markHeight + stack * markStack + H(HeightTime(s, s.mode, ft)));
            }
        }
    }
}
