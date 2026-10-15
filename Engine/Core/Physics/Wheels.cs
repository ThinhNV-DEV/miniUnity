// WheelCollider for MiniUnity's 3D physics: a raycast wheel in the spirit of the PhysX vehicle model Unity wraps.
// Every physics step, before the rigid bodies are integrated, each wheel casts from its suspension anchor down along
// the attached Rigidbody's up axis for suspensionDistance + radius. On a hit:
//   suspension  F = sprungMass·|g| + spring·(compression − targetPosition·suspensionDistance) + damper·compression rate
//               (≥ 0, applied at the anchor along the suspension axis; equilibrium at the target position)
//   tire        longitudinal slip (ω·r − v_fwd) / max(|v_fwd|, 1), lateral slip v_side / max(|v_fwd|, 1);
//               friction from the WheelFrictionCurve (Hermite rise to the extremum, then to the asymptote) × load ×
//               stiffness, capped at the force that cancels the slip velocity of the sprung mass in one step
//   wheel spin  I = ½·mass·r²; motor torque, brake torque (never reverses the wheel), tire reaction, wheelDampingRate.
// Wheels are not part of the collision scene (rigid bodies and queries ignore them), as in Unity.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity.Physics3D;
using MiniUnity.Yaml;
using UnityEngine;

namespace UnityEngine
{
    public partial class WheelCollider
    {
        internal float m_AngVel, m_SpinAngle, m_Compression, m_PrevCompression, m_SprungMass, m_Motor, m_Brake, m_Steer;
        internal bool m_Grounded, m_HasPrev;
        internal WheelHit m_Hit;

        public WheelCollider() { }

        static WheelFrictionCurve Curve(Object o, string k, float es, float ev, float aSlip, float av)
        {
            var c = (Collider)o;
            return new WheelFrictionCurve
            {
                extremumSlip = c.NFloat(k + ".m_ExtremumSlip", es), extremumValue = c.NFloat(k + ".m_ExtremumValue", ev),
                asymptoteSlip = c.NFloat(k + ".m_AsymptoteSlip", aSlip), asymptoteValue = c.NFloat(k + ".m_AsymptoteValue", av),
                stiffness = c.NFloat(k + ".m_Stiffness", 1),
            };
        }
        void SetCurve(string k, WheelFrictionCurve v)
        {
            NSetF(k + ".m_ExtremumSlip", v.extremumSlip); NSetF(k + ".m_ExtremumValue", v.extremumValue);
            NSetF(k + ".m_AsymptoteSlip", v.asymptoteSlip); NSetF(k + ".m_AsymptoteValue", v.asymptoteValue); NSetF(k + ".m_Stiffness", v.stiffness);
        }

        public Vector3 center { get => NVec3("m_Center"); set => NSetVec3("m_Center", value); }
        public float radius { get => NFloat("m_Radius", 0.5f); set => NSetF("m_Radius", value); }
        public float suspensionDistance { get => NFloat("m_SuspensionDistance", 0.3f); set => NSetF("m_SuspensionDistance", value); }
        public JointSpring suspensionSpring
        {
            get => new JointSpring { spring = NFloat("m_SuspensionSpring.spring", 35000), damper = NFloat("m_SuspensionSpring.damper", 4500), targetPosition = NFloat("m_SuspensionSpring.targetPosition", 0.5f) };
            set { NSetF("m_SuspensionSpring.spring", value.spring); NSetF("m_SuspensionSpring.damper", value.damper); NSetF("m_SuspensionSpring.targetPosition", value.targetPosition); }
        }
        public bool suspensionExpansionLimited { get => NBool("m_SuspensionExpansionLimited"); set => NSetB("m_SuspensionExpansionLimited", value); }
        public float forceAppPointDistance { get => NFloat("m_ForceAppPointDistance"); set => NSetF("m_ForceAppPointDistance", value); }
        public float mass { get => NFloat("m_Mass", 20); set => NSetF("m_Mass", Math.Max(1e-4f, value)); }
        public float wheelDampingRate { get => NFloat("m_WheelDampingRate", 0.25f); set => NSetF("m_WheelDampingRate", value); }
        public WheelFrictionCurve forwardFriction { get => Curve(this, "m_ForwardFriction", 0.4f, 1, 0.8f, 0.5f); set => SetCurve("m_ForwardFriction", value); }
        public WheelFrictionCurve sidewaysFriction { get => Curve(this, "m_SidewaysFriction", 0.2f, 1, 0.5f, 0.75f); set => SetCurve("m_SidewaysFriction", value); }
        public float motorTorque { get => m_Motor; set => m_Motor = value; }
        public float brakeTorque { get => m_Brake; set => m_Brake = Math.Max(0, value); }
        public float steerAngle { get => m_Steer; set => m_Steer = value; }
        public bool isGrounded => m_Grounded;
        public float rpm => m_AngVel * 60f / (2f * Mathf.PI);
        public float rotationSpeed => m_AngVel * Mathf.Rad2Deg;
        public float sprungMass { get => m_SprungMass; set => m_SprungMass = value; }

        public void ResetSprungMasses() => m_SprungMass = 0;
        public void ConfigureVehicleSubsteps(float speedThreshold, int stepsBelowThreshold, int stepsAboveThreshold) { }

        public bool GetGroundHit(out WheelHit hit) { hit = m_Hit; return m_Grounded; }

        public void GetWorldPose(out Vector3 pos, out Quaternion quat)
        {
            var t = transform;
            var anchor = t.TransformPoint(center);
            var up = t.up;
            pos = anchor - up * (suspensionDistance - m_Compression);
            quat = t.rotation * Quaternion.Euler(0, m_Steer, 0) * Quaternion.Euler(m_SpinAngle, 0, 0);
        }
    }

    public partial struct WheelHit
    {
        internal Collider m_Collider; internal Vector3 m_Point, m_Normal, m_ForwardDir, m_SidewaysDir; internal float m_Force, m_ForwardSlip, m_SidewaysSlip;
        public Collider collider { get => m_Collider; set => m_Collider = value; }
        public Vector3 point { get => m_Point; set => m_Point = value; }
        public Vector3 normal { get => m_Normal; set => m_Normal = value; }
        public Vector3 forwardDir { get => m_ForwardDir; set => m_ForwardDir = value; }
        public Vector3 sidewaysDir { get => m_SidewaysDir; set => m_SidewaysDir = value; }
        public float force { get => m_Force; set => m_Force = value; }
        public float forwardSlip { get => m_ForwardSlip; set => m_ForwardSlip = value; }
        public float sidewaysSlip { get => m_SidewaysSlip; set => m_SidewaysSlip = value; }
    }
}

namespace MiniUnity.Physics3D
{
    public static class WheelSystem
    {
        /// <summary>WheelFrictionCurve value at a slip (symmetric).</summary>
        public static float Friction(WheelFrictionCurve c, float slip)
        {
            float s = Math.Abs(slip);
            float es = Math.Max(1e-4f, c.extremumSlip), aSlip = Math.Max(es + 1e-4f, c.asymptoteSlip);
            if (s <= es) { float t = s / es; return c.extremumValue * t * (2 - t); }
            if (s >= aSlip) return c.asymptoteValue;
            float u = (s - es) / (aSlip - es);
            return c.extremumValue + (c.asymptoteValue - c.extremumValue) * u * u * (3 - 2 * u);
        }

        public static void Step(IEnumerable<WheelCollider> wheels, float dt)
        {
            var list = wheels.Where(w => w != null && !w.m_Destroyed && w.enabled && w.gameObject.activeInHierarchy).ToList();
            var perBody = list.GroupBy(w => w.attachedRigidbody).Where(g => g.Key != null).ToDictionary(g => g.Key, g => g.Count());
            float g = PhysicsWorld.Gravity.magnitude;
            foreach (var w in list)
            {
                var rb = w.attachedRigidbody;
                if (rb == null) continue;
                var body = PhysicsWorld.BodyOf(rb);
                if (body == null || !body.Dynamic) continue;
                if (w.m_SprungMass <= 0) w.m_SprungMass = rb.mass / perBody[rb];
                var t = w.transform;
                var up = t.up;
                var anchor = t.TransformPoint(w.center);
                float r = w.radius, susp = w.suspensionDistance;
                RaycastHit hit = default; bool grounded = false;
                foreach (var h in PhysicsWorld.Raycast(anchor, -up, susp + r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore, true))
                {
                    if (h.Shape.Body == body) continue;
                    hit = RaycastHit.From(h); grounded = true; break;
                }
                float I = 0.5f * w.mass * r * r;
                float damping = w.wheelDampingRate;
                if (!grounded)
                {
                    w.m_Grounded = false; w.m_Compression = 0; w.m_HasPrev = false; w.m_Hit = default;
                    w.m_AngVel += (w.m_Motor - damping * w.m_AngVel) / I * dt;
                    w.m_AngVel = ApplyBrake(w.m_AngVel, w.m_Brake / I * dt);
                    w.m_SpinAngle = (w.m_SpinAngle + w.m_AngVel * Mathf.Rad2Deg * dt) % 360f;
                    continue;
                }
                float compression = Mathf.Clamp(susp - (hit.distance - r), 0, susp);
                float rate = w.m_HasPrev ? (compression - w.m_PrevCompression) / dt : 0;
                w.m_PrevCompression = compression; w.m_HasPrev = true; w.m_Compression = compression;
                var spring = w.suspensionSpring;
                float load = Math.Max(0, w.m_SprungMass * g + spring.spring * (compression - spring.targetPosition * susp) + spring.damper * rate);

                var steer = Quaternion.AngleAxis(w.m_Steer, up);
                var n = hit.normal;
                var fwd = Vector3.ProjectOnPlane(steer * t.forward, n).normalized;
                var side = Vector3.Cross(n, fwd).normalized;
                var contact = hit.point;
                var v = rb.GetPointVelocity(contact);
                float vLong = Vector3.Dot(v, fwd), vLat = Vector3.Dot(v, side);
                float denom = Math.Max(Math.Abs(vLong), 1f);

                // wheel spin from motor / brake / damping, then the tire force from the resulting slip
                w.m_AngVel += (w.m_Motor - damping * w.m_AngVel) / I * dt;
                w.m_AngVel = ApplyBrake(w.m_AngVel, w.m_Brake / I * dt);
                float slipLong = (w.m_AngVel * r - vLong) / denom;
                float slipLat = vLat / denom;
                var ff = w.forwardFriction; var sf = w.sidewaysFriction;
                float fLong = Math.Sign(slipLong) * Friction(ff, slipLong) * load * ff.stiffness;
                float fLat = -Math.Sign(slipLat) * Friction(sf, slipLat) * load * sf.stiffness;
                // never more than what stops the slip of the sprung mass in this step
                float cap = w.m_SprungMass / dt;
                fLat = Mathf.Clamp(fLat, -Math.Abs(vLat) * cap, Math.Abs(vLat) * cap);
                float slipVel = w.m_AngVel * r - vLong;
                float mEff = 1f / (1f / w.m_SprungMass + r * r / I);
                fLong = Mathf.Clamp(fLong, -Math.Abs(slipVel) * mEff / dt, Math.Abs(slipVel) * mEff / dt);
                w.m_AngVel -= fLong * r / I * dt;

                var at = anchor - up * (susp - compression) - up * (r - w.forceAppPointDistance);
                rb.AddForceAtPosition(up * load, anchor, ForceMode.Force);
                rb.AddForceAtPosition(fwd * fLong + side * fLat, at, ForceMode.Force);

                w.m_Grounded = true;
                w.m_SpinAngle = (w.m_SpinAngle + w.m_AngVel * Mathf.Rad2Deg * dt) % 360f;
                w.m_Hit = new WheelHit
                {
                    m_Collider = hit.collider, m_Point = contact, m_Normal = n, m_ForwardDir = fwd, m_SidewaysDir = side,
                    m_Force = load, m_ForwardSlip = slipLong, m_SidewaysSlip = slipLat,
                };
            }
        }

        static float ApplyBrake(float w, float dw) => Math.Abs(w) <= dw ? 0 : w - Math.Sign(w) * dw;
    }
}
