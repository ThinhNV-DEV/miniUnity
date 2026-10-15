// 2D joints for MiniUnity's Physics2D, ported from Box2D v2.4.1 (revolute, prismatic, distance, weld, mouse, wheel,
// friction, motor joints). Body A is the connected body (or the static ground), body B the joint's own Rigidbody2D.
//
// Box2D — MIT License, Copyright (c) 2019 Erin Catto. See B2Collision.cs for the full license text.
using System;
using UnityEngine;

namespace MiniUnity.Box2D
{
    struct Mat22
    {
        public float a11, a12, a21, a22;   // [ex ey] with ex = (a11, a21), ey = (a12, a22)
        public Vector2 Solve(Vector2 b)
        {
            float det = a11 * a22 - a12 * a21; if (det != 0) det = 1 / det;
            return new Vector2(det * (a22 * b.x - a12 * b.y), det * (a11 * b.y - a21 * b.x));
        }
        public Mat22 Inverse()
        {
            float det = a11 * a22 - a12 * a21; if (det != 0) det = 1 / det;
            return new Mat22 { a11 = det * a22, a12 = -det * a12, a21 = -det * a21, a22 = det * a11 };
        }
        public Vector2 Mul(Vector2 v) => new(a11 * v.x + a12 * v.y, a21 * v.x + a22 * v.y);
    }

    struct Mat33
    {
        public Vector3 ex, ey, ez;
        public Vector3 Solve33(Vector3 b)
        {
            float det = Vector3.Dot(ex, Vector3.Cross(ey, ez)); if (det != 0) det = 1 / det;
            return new Vector3(det * Vector3.Dot(b, Vector3.Cross(ey, ez)), det * Vector3.Dot(ex, Vector3.Cross(b, ez)), det * Vector3.Dot(ex, Vector3.Cross(ey, b)));
        }
        public Vector2 Solve22(Vector2 b)
        {
            float a11 = ex.x, a12 = ey.x, a21 = ex.y, a22 = ey.y;
            float det = a11 * a22 - a12 * a21; if (det != 0) det = 1 / det;
            return new Vector2(det * (a22 * b.x - a12 * b.y), det * (a11 * b.y - a21 * b.x));
        }
        public Mat33 Inverse22()
        {
            float a = ex.x, b = ey.x, c = ex.y, d = ey.y;
            float det = a * d - b * c; if (det != 0) det = 1 / det;
            return new Mat33 { ex = new Vector3(det * d, -det * c, 0), ey = new Vector3(-det * b, det * a, 0), ez = Vector3.zero };
        }
        public Mat33 SymInverse33()
        {
            float det = Vector3.Dot(ex, Vector3.Cross(ey, ez)); if (det != 0) det = 1 / det;
            float a11 = ex.x, a12 = ey.x, a13 = ez.x, a22 = ey.y, a23 = ez.y, a33 = ez.z;
            var M = new Mat33();
            M.ex = new Vector3(det * (a22 * a33 - a23 * a23), det * (a13 * a23 - a12 * a33), det * (a12 * a23 - a13 * a22));
            M.ey = new Vector3(M.ex.y, det * (a11 * a33 - a13 * a13), det * (a13 * a12 - a11 * a23));
            M.ez = new Vector3(M.ex.z, M.ey.z, det * (a11 * a22 - a12 * a12));
            return M;
        }
        public Vector3 Mul(Vector3 v) => v.x * ex + v.y * ey + v.z * ez;
        public Vector2 Mul22(Vector2 v) => new(ex.x * v.x + ey.x * v.y, ex.y * v.x + ey.y * v.y);
    }

    public abstract class AnchoredJoint2 : Joint2
    {
        public Vector2 LocalAnchorA, LocalAnchorB;
        public float ReferenceAngle;
        public bool AnchorConfigured;
        protected Vector2 rA, rB, lcA, lcB; protected float mA, mB, iA, iB;

        protected void Prepare()
        {
            lcA = A.LocalCenter; lcB = B.LocalCenter;
            mA = A.InvMass; mB = B.InvMass; iA = A.InvI; iB = B.InvI;
            rA = new Rot(A.SA).Mul(LocalAnchorA - lcA); rB = new Rot(B.SA).Mul(LocalAnchorB - lcB);
        }
        protected void ApplyLinear(Vector2 P, float angA = 0, float angB = 0)
        {
            A.SV -= mA * P; A.SW -= iA * (B2.Cross(rA, P) + angA);
            B.SV += mB * P; B.SW += iB * (B2.Cross(rB, P) + angB);
        }

        public static void LinearStiffness(out float stiffness, out float damping, float hz, float ratio, Body2 a, Body2 b)
        {
            float mA = a.IsDynamic ? a.Mass : 0, mB = b.IsDynamic ? b.Mass : 0;
            float mass = mA > 0 && mB > 0 ? mA * mB / (mA + mB) : mA > 0 ? mA : mB;
            float omega = 2 * MathF.PI * hz;
            stiffness = mass * omega * omega; damping = 2 * mass * ratio * omega;
        }
        public static void AngularStiffness(out float stiffness, out float damping, float hz, float ratio, Body2 a, Body2 b)
        {
            float IA = a.IsDynamic && a.InvI > 0 ? a.I : 0, IB = b.IsDynamic && b.InvI > 0 ? b.I : 0;
            float I = IA > 0 && IB > 0 ? IA * IB / (IA + IB) : IA > 0 ? IA : IB;
            float omega = 2 * MathF.PI * hz;
            stiffness = I * omega * omega; damping = 2 * I * ratio * omega;
        }
        public Vector2 WorldAnchorA => A.Xf.Mul(LocalAnchorA);
        public Vector2 WorldAnchorB => B.Xf.Mul(LocalAnchorB);
    }

    // ------------------------------------------------------------------------------------------- revolute (HingeJoint2D)
    public sealed class RevoluteJoint2 : AnchoredJoint2
    {
        public bool EnableLimit, EnableMotor;
        public float LowerAngle, UpperAngle, MotorSpeed, MaxMotorTorque;
        Vector2 impulse; float motorImpulse, lowerImpulse, upperImpulse, axialMass, angle;
        Mat22 K;
        public float MotorImpulse => motorImpulse;
        public float JointAngle => B.A - A.A - ReferenceAngle;
        public float JointSpeed => B.W - A.W;

        public override void InitVelocity(in TimeStep2 step)
        {
            Prepare();
            K.a11 = mA + mB + rA.y * rA.y * iA + rB.y * rB.y * iB;
            K.a12 = -rA.y * rA.x * iA - rB.y * rB.x * iB; K.a21 = K.a12;
            K.a22 = mA + mB + rA.x * rA.x * iA + rB.x * rB.x * iB;
            axialMass = iA + iB;
            bool fixedRot = axialMass <= 0;
            if (!fixedRot) axialMass = 1 / axialMass;
            angle = B.SA - A.SA - ReferenceAngle;
            if (!EnableLimit || fixedRot) { lowerImpulse = upperImpulse = 0; }
            if (!EnableMotor || fixedRot) motorImpulse = 0;
            if (step.WarmStarting)
            {
                impulse *= step.DtRatio; motorImpulse *= step.DtRatio; lowerImpulse *= step.DtRatio; upperImpulse *= step.DtRatio;
                float axial = motorImpulse + lowerImpulse - upperImpulse;
                ApplyLinear(impulse, axial, axial);
            }
            else { impulse = Vector2.zero; motorImpulse = lowerImpulse = upperImpulse = 0; }
        }

        public override void SolveVelocity(in TimeStep2 step)
        {
            bool fixedRot = iA + iB == 0;
            if (EnableMotor && !fixedRot)
            {
                float Cdot = B.SW - A.SW - MotorSpeed;
                float imp = -axialMass * Cdot, old = motorImpulse, max = step.Dt * MaxMotorTorque;
                motorImpulse = Math.Clamp(motorImpulse + imp, -max, max); imp = motorImpulse - old;
                A.SW -= iA * imp; B.SW += iB * imp;
            }
            if (EnableLimit && !fixedRot)
            {
                {
                    float C = angle - LowerAngle, Cdot = B.SW - A.SW;
                    float imp = -axialMass * (Cdot + Math.Max(C, 0) * step.InvDt), old = lowerImpulse;
                    lowerImpulse = Math.Max(lowerImpulse + imp, 0); imp = lowerImpulse - old;
                    A.SW -= iA * imp; B.SW += iB * imp;
                }
                {
                    float C = UpperAngle - angle, Cdot = A.SW - B.SW;
                    float imp = -axialMass * (Cdot + Math.Max(C, 0) * step.InvDt), old = upperImpulse;
                    upperImpulse = Math.Max(upperImpulse + imp, 0); imp = upperImpulse - old;
                    A.SW += iA * imp; B.SW -= iB * imp;
                }
            }
            var cd = B.SV + B2.Cross(B.SW, rB) - A.SV - B2.Cross(A.SW, rA);
            var im = K.Solve(-cd);
            impulse += im;
            ApplyLinear(im);
        }

        public override bool SolvePosition(in TimeStep2 step)
        {
            float angularError = 0, positionError;
            bool fixedRot = A.InvI + B.InvI == 0;
            float aA = A.SA, aB = B.SA;
            if (EnableLimit && !fixedRot)
            {
                float ang = aB - aA - ReferenceAngle, C = 0;
                if (Math.Abs(UpperAngle - LowerAngle) < 2 * B2.AngularSlop) C = Math.Clamp(ang - LowerAngle, -MaxAngularCorrection, MaxAngularCorrection);
                else if (ang <= LowerAngle) C = Math.Clamp(ang - LowerAngle + B2.AngularSlop, -MaxAngularCorrection, 0);
                else if (ang >= UpperAngle) C = Math.Clamp(ang - UpperAngle - B2.AngularSlop, 0, MaxAngularCorrection);
                float limitImpulse = -axialMass * C;
                A.SA -= A.InvI * limitImpulse; B.SA += B.InvI * limitImpulse;
                angularError = Math.Abs(C);
            }
            {
                var ra = new Rot(A.SA).Mul(LocalAnchorA - lcA); var rb = new Rot(B.SA).Mul(LocalAnchorB - lcB);
                var C = B.SC + rb - A.SC - ra;
                positionError = C.magnitude;
                var k = new Mat22
                {
                    a11 = mA + mB + iA * ra.y * ra.y + iB * rb.y * rb.y, a12 = -iA * ra.x * ra.y - iB * rb.x * rb.y,
                    a22 = mA + mB + iA * ra.x * ra.x + iB * rb.x * rb.x,
                };
                k.a21 = k.a12;
                var imp = -k.Solve(C);
                A.SC -= mA * imp; A.SA -= iA * B2.Cross(ra, imp);
                B.SC += mB * imp; B.SA += iB * B2.Cross(rb, imp);
            }
            return positionError <= B2.LinearSlop && angularError <= B2.AngularSlop;
        }

        public override Vector2 ReactionForce(float invDt) => invDt * impulse;
        public override float ReactionTorque(float invDt) => invDt * (motorImpulse + lowerImpulse - upperImpulse);
    }

    // ------------------------------------------------------------------------------------------- distance (Distance / SpringJoint2D)
    public sealed class DistanceJoint2 : AnchoredJoint2
    {
        public float Length = 1, MinLength = 1, MaxLength = 1, Stiffness, Damping;
        Vector2 u; float currentLength, mass, softMass, gamma, bias, impulse, lowerImpulse, upperImpulse;
        public float CurrentLength => (WorldAnchorB - WorldAnchorA).magnitude;

        public override void InitVelocity(in TimeStep2 step)
        {
            Prepare();
            u = B.SC + rB - A.SC - rA;
            currentLength = u.magnitude;
            if (currentLength > B2.LinearSlop) u *= 1 / currentLength;
            else { u = Vector2.zero; mass = 0; impulse = lowerImpulse = upperImpulse = 0; }
            float crAu = B2.Cross(rA, u), crBu = B2.Cross(rB, u);
            float invMass = mA + iA * crAu * crAu + mB + iB * crBu * crBu;
            mass = invMass != 0 ? 1 / invMass : 0;
            if (Stiffness > 0 && MinLength < MaxLength)
            {
                float C = currentLength - Length, h = step.Dt;
                gamma = h * (Damping + h * Stiffness); gamma = gamma != 0 ? 1 / gamma : 0;
                bias = C * h * Stiffness * gamma;
                invMass += gamma; softMass = invMass != 0 ? 1 / invMass : 0;
            }
            else { gamma = 0; bias = 0; softMass = mass; }
            if (step.WarmStarting)
            {
                impulse *= step.DtRatio; lowerImpulse *= step.DtRatio; upperImpulse *= step.DtRatio;
                ApplyLinear((impulse + lowerImpulse - upperImpulse) * u);
            }
            else impulse = 0;
        }

        float Cdot() => B2.Dot(u, B.SV + B2.Cross(B.SW, rB) - A.SV - B2.Cross(A.SW, rA));

        public override void SolveVelocity(in TimeStep2 step)
        {
            if (MinLength < MaxLength)
            {
                if (Stiffness > 0)
                {
                    float imp = -softMass * (Cdot() + bias + gamma * impulse);
                    impulse += imp; ApplyLinear(imp * u);
                }
                {
                    float C = currentLength - MinLength, b = Math.Max(0, C) * step.InvDt;
                    float imp = -mass * (Cdot() + b), old = lowerImpulse;
                    lowerImpulse = Math.Max(0, lowerImpulse + imp); imp = lowerImpulse - old;
                    ApplyLinear(imp * u);
                }
                {
                    float C = MaxLength - currentLength, b = Math.Max(0, C) * step.InvDt;
                    float imp = -mass * (-Cdot() + b), old = upperImpulse;
                    upperImpulse = Math.Max(0, upperImpulse + imp); imp = upperImpulse - old;
                    ApplyLinear(-imp * u);
                }
            }
            else
            {
                float imp = -mass * Cdot();
                impulse += imp; ApplyLinear(imp * u);
            }
        }

        public override bool SolvePosition(in TimeStep2 step)
        {
            var ra = new Rot(A.SA).Mul(LocalAnchorA - lcA); var rb = new Rot(B.SA).Mul(LocalAnchorB - lcB);
            var d = B.SC + rb - A.SC - ra; float length = d.magnitude; var uu = length > B2.Epsilon ? d / length : Vector2.zero;
            float C;
            if (MinLength == MaxLength) C = length - MinLength;
            else if (length < MinLength) C = length - MinLength;
            else if (MaxLength < length) C = length - MaxLength;
            else return true;
            var P = -mass * C * uu;
            A.SC -= mA * P; A.SA -= iA * B2.Cross(ra, P);
            B.SC += mB * P; B.SA += iB * B2.Cross(rb, P);
            return Math.Abs(C) < B2.LinearSlop;
        }

        public override Vector2 ReactionForce(float invDt) => invDt * (impulse + lowerImpulse - upperImpulse) * u;
        public override float ReactionTorque(float invDt) => 0;
    }

    // ------------------------------------------------------------------------------------------- weld (FixedJoint2D)
    public sealed class WeldJoint2 : AnchoredJoint2
    {
        public float Stiffness, Damping;
        Vector3 impulse; float gamma, bias; Mat33 mass;

        Mat33 KOf(Vector2 ra, Vector2 rb) => new()
        {
            ex = new Vector3(mA + mB + ra.y * ra.y * iA + rb.y * rb.y * iB, -ra.y * ra.x * iA - rb.y * rb.x * iB, -ra.y * iA - rb.y * iB),
            ey = new Vector3(-ra.y * ra.x * iA - rb.y * rb.x * iB, mA + mB + ra.x * ra.x * iA + rb.x * rb.x * iB, ra.x * iA + rb.x * iB),
            ez = new Vector3(-ra.y * iA - rb.y * iB, ra.x * iA + rb.x * iB, iA + iB),
        };

        public override void InitVelocity(in TimeStep2 step)
        {
            Prepare();
            var K = KOf(rA, rB);
            if (Stiffness > 0)
            {
                mass = K.Inverse22();
                float invM = iA + iB, C = B.SA - A.SA - ReferenceAngle, h = step.Dt;
                gamma = h * (Damping + h * Stiffness); gamma = gamma != 0 ? 1 / gamma : 0;
                bias = C * h * Stiffness * gamma;
                invM += gamma; mass.ez.z = invM != 0 ? 1 / invM : 0;
            }
            else if (K.ez.z == 0) { mass = K.Inverse22(); gamma = 0; bias = 0; }
            else { mass = K.SymInverse33(); gamma = 0; bias = 0; }
            if (step.WarmStarting) { impulse *= step.DtRatio; ApplyLinear(new Vector2(impulse.x, impulse.y), impulse.z, impulse.z); }
            else impulse = Vector3.zero;
        }

        public override void SolveVelocity(in TimeStep2 step)
        {
            if (Stiffness > 0)
            {
                float Cdot2 = B.SW - A.SW;
                float imp2 = -mass.ez.z * (Cdot2 + bias + gamma * impulse.z);
                impulse.z += imp2; A.SW -= iA * imp2; B.SW += iB * imp2;
                var Cdot1 = B.SV + B2.Cross(B.SW, rB) - A.SV - B2.Cross(A.SW, rA);
                var imp1 = -mass.Mul22(Cdot1);
                impulse.x += imp1.x; impulse.y += imp1.y;
                ApplyLinear(imp1);
            }
            else
            {
                var Cdot1 = B.SV + B2.Cross(B.SW, rB) - A.SV - B2.Cross(A.SW, rA);
                var imp = -mass.Mul(new Vector3(Cdot1.x, Cdot1.y, B.SW - A.SW));
                impulse += imp;
                ApplyLinear(new Vector2(imp.x, imp.y), imp.z, imp.z);
            }
        }

        public override bool SolvePosition(in TimeStep2 step)
        {
            var ra = new Rot(A.SA).Mul(LocalAnchorA - lcA); var rb = new Rot(B.SA).Mul(LocalAnchorB - lcB);
            var K = KOf(ra, rb);
            float positionError, angularError;
            var C1 = B.SC + rb - A.SC - ra;
            Vector3 imp;
            if (Stiffness > 0) { positionError = C1.magnitude; angularError = 0; var p = -K.Solve22(C1); imp = new Vector3(p.x, p.y, 0); }
            else
            {
                float C2 = B.SA - A.SA - ReferenceAngle;
                positionError = C1.magnitude; angularError = Math.Abs(C2);
                if (K.ez.z > 0) imp = -K.Solve33(new Vector3(C1.x, C1.y, C2));
                else { var p = -K.Solve22(C1); imp = new Vector3(p.x, p.y, 0); }
            }
            var P = new Vector2(imp.x, imp.y);
            A.SC -= mA * P; A.SA -= iA * (B2.Cross(ra, P) + imp.z);
            B.SC += mB * P; B.SA += iB * (B2.Cross(rb, P) + imp.z);
            return positionError <= B2.LinearSlop && angularError <= B2.AngularSlop;
        }

        public override Vector2 ReactionForce(float invDt) => invDt * new Vector2(impulse.x, impulse.y);
        public override float ReactionTorque(float invDt) => invDt * impulse.z;
    }

    // ------------------------------------------------------------------------------------------- mouse (TargetJoint2D)
    public sealed class MouseJoint2 : Joint2
    {
        public Vector2 LocalAnchorB, Target; public float MaxForce, Stiffness, Damping;
        Vector2 impulse, rB, C; float gamma, beta, mB, iB; Mat22 mass;

        public override void InitVelocity(in TimeStep2 step)
        {
            mB = B.InvMass; iB = B.InvI;
            float h = step.Dt;
            gamma = h * (Damping + h * Stiffness); if (gamma != 0) gamma = 1 / gamma;
            beta = h * Stiffness * gamma;
            rB = new Rot(B.SA).Mul(LocalAnchorB - B.LocalCenter);
            var K = new Mat22 { a11 = mB + iB * rB.y * rB.y + gamma, a21 = -iB * rB.x * rB.y, a22 = mB + iB * rB.x * rB.x + gamma };
            K.a12 = K.a21;
            mass = K.Inverse();
            C = (B.SC + rB - Target) * beta;
            B.SW *= 0.98f;
            if (step.WarmStarting) { impulse *= step.DtRatio; B.SV += mB * impulse; B.SW += iB * B2.Cross(rB, impulse); }
            else impulse = Vector2.zero;
        }

        public override void SolveVelocity(in TimeStep2 step)
        {
            var Cdot = B.SV + B2.Cross(B.SW, rB);
            var imp = mass.Mul(-(Cdot + C + gamma * impulse));
            var old = impulse; impulse += imp;
            float max = step.Dt * MaxForce;
            if (impulse.sqrMagnitude > max * max) impulse *= max / impulse.magnitude;
            imp = impulse - old;
            B.SV += mB * imp; B.SW += iB * B2.Cross(rB, imp);
        }

        public override bool SolvePosition(in TimeStep2 step) => true;
        public override Vector2 ReactionForce(float invDt) => invDt * impulse;
        public override float ReactionTorque(float invDt) => 0;
    }

    // ------------------------------------------------------------------------------------------- prismatic (SliderJoint2D)
    public sealed class PrismaticJoint2 : AnchoredJoint2
    {
        public Vector2 LocalXAxisA = Vector2.right;
        public bool EnableLimit, EnableMotor;
        public float Lower, Upper, MotorSpeed, MaxMotorForce;
        Vector2 impulse, axis, perp; float motorImpulse, lowerImpulse, upperImpulse, s1, s2, a1, a2, axialMass, translation;
        Mat22 K;
        Vector2 LocalYAxisA => B2.Cross(1f, LocalXAxisA);
        public float MotorImpulse => motorImpulse;
        public float JointTranslation => B2.Dot(A.Xf.q.Mul(LocalXAxisA), WorldAnchorB - WorldAnchorA);
        public float JointSpeed
        {
            get
            {
                var ra = A.Xf.q.Mul(LocalAnchorA - A.LocalCenter); var rb = B.Xf.q.Mul(LocalAnchorB - B.LocalCenter);
                var p1 = A.C + ra; var p2 = B.C + rb; var d = p2 - p1; var ax = A.Xf.q.Mul(LocalXAxisA);
                return B2.Dot(d, B2.Cross(A.W, ax)) + B2.Dot(ax, B.V + B2.Cross(B.W, rb) - A.V - B2.Cross(A.W, ra));
            }
        }

        void ApplyAxial(Vector2 P, float LA, float LB) { A.SV -= mA * P; A.SW -= iA * LA; B.SV += mB * P; B.SW += iB * LB; }

        public override void InitVelocity(in TimeStep2 step)
        {
            Prepare();
            var d = B.SC - A.SC + rB - rA;
            var qA = new Rot(A.SA);
            axis = qA.Mul(LocalXAxisA); a1 = B2.Cross(d + rA, axis); a2 = B2.Cross(rB, axis);
            axialMass = mA + mB + iA * a1 * a1 + iB * a2 * a2; if (axialMass > 0) axialMass = 1 / axialMass;
            perp = qA.Mul(LocalYAxisA); s1 = B2.Cross(d + rA, perp); s2 = B2.Cross(rB, perp);
            float k22 = iA + iB; if (k22 == 0) k22 = 1;
            K = new Mat22 { a11 = mA + mB + iA * s1 * s1 + iB * s2 * s2, a12 = iA * s1 + iB * s2, a21 = iA * s1 + iB * s2, a22 = k22 };
            if (EnableLimit) translation = B2.Dot(axis, d); else lowerImpulse = upperImpulse = 0;
            if (!EnableMotor) motorImpulse = 0;
            if (step.WarmStarting)
            {
                impulse *= step.DtRatio; motorImpulse *= step.DtRatio; lowerImpulse *= step.DtRatio; upperImpulse *= step.DtRatio;
                float axial = motorImpulse + lowerImpulse - upperImpulse;
                ApplyAxial(impulse.x * perp + axial * axis, impulse.x * s1 + impulse.y + axial * a1, impulse.x * s2 + impulse.y + axial * a2);
            }
            else { impulse = Vector2.zero; motorImpulse = lowerImpulse = upperImpulse = 0; }
        }

        float AxialCdot() => B2.Dot(axis, B.SV - A.SV) + a2 * B.SW - a1 * A.SW;

        public override void SolveVelocity(in TimeStep2 step)
        {
            if (EnableMotor)
            {
                float imp = axialMass * (MotorSpeed - AxialCdot()), old = motorImpulse, max = step.Dt * MaxMotorForce;
                motorImpulse = Math.Clamp(motorImpulse + imp, -max, max); imp = motorImpulse - old;
                ApplyAxial(imp * axis, imp * a1, imp * a2);
            }
            if (EnableLimit)
            {
                {
                    float C = translation - Lower;
                    float imp = -axialMass * (AxialCdot() + Math.Max(C, 0) * step.InvDt), old = lowerImpulse;
                    lowerImpulse = Math.Max(lowerImpulse + imp, 0); imp = lowerImpulse - old;
                    ApplyAxial(imp * axis, imp * a1, imp * a2);
                }
                {
                    float C = Upper - translation;
                    float imp = -axialMass * (-AxialCdot() + Math.Max(C, 0) * step.InvDt), old = upperImpulse;
                    upperImpulse = Math.Max(upperImpulse + imp, 0); imp = upperImpulse - old;
                    ApplyAxial(-imp * axis, -imp * a1, -imp * a2);
                }
            }
            var Cdot = new Vector2(B2.Dot(perp, B.SV - A.SV) + s2 * B.SW - s1 * A.SW, B.SW - A.SW);
            var df = K.Solve(-Cdot);
            impulse += df;
            ApplyAxial(df.x * perp, df.x * s1 + df.y, df.x * s2 + df.y);
        }

        public override bool SolvePosition(in TimeStep2 step)
        {
            var qA = new Rot(A.SA); var qB = new Rot(B.SA);
            var ra = qA.Mul(LocalAnchorA - lcA); var rb = qB.Mul(LocalAnchorB - lcB);
            var d = B.SC + rb - A.SC - ra;
            var ax = qA.Mul(LocalXAxisA); float aa1 = B2.Cross(d + ra, ax), aa2 = B2.Cross(rb, ax);
            var pp = qA.Mul(LocalYAxisA); float ss1 = B2.Cross(d + ra, pp), ss2 = B2.Cross(rb, pp);
            var C1 = new Vector2(B2.Dot(pp, d), B.SA - A.SA - ReferenceAngle);
            float linearError = Math.Abs(C1.x), angularError = Math.Abs(C1.y);
            bool active = false; float C2 = 0;
            if (EnableLimit)
            {
                float tr = B2.Dot(ax, d);
                if (Math.Abs(Upper - Lower) < 2 * B2.LinearSlop) { C2 = tr; linearError = Math.Max(linearError, Math.Abs(tr)); active = true; }
                else if (tr <= Lower) { C2 = Math.Min(tr - Lower, 0); linearError = Math.Max(linearError, Lower - tr); active = true; }
                else if (tr >= Upper) { C2 = Math.Max(tr - Upper, 0); linearError = Math.Max(linearError, tr - Upper); active = true; }
            }
            Vector3 imp;
            float k11 = mA + mB + iA * ss1 * ss1 + iB * ss2 * ss2, k12 = iA * ss1 + iB * ss2, k22 = iA + iB; if (k22 == 0) k22 = 1;
            if (active)
            {
                float k13 = iA * ss1 * aa1 + iB * ss2 * aa2, k23 = iA * aa1 + iB * aa2, k33 = mA + mB + iA * aa1 * aa1 + iB * aa2 * aa2;
                var K3 = new Mat33 { ex = new Vector3(k11, k12, k13), ey = new Vector3(k12, k22, k23), ez = new Vector3(k13, k23, k33) };
                imp = K3.Solve33(-new Vector3(C1.x, C1.y, C2));
            }
            else
            {
                var K2 = new Mat22 { a11 = k11, a12 = k12, a21 = k12, a22 = k22 };
                var i1 = K2.Solve(-C1); imp = new Vector3(i1.x, i1.y, 0);
            }
            var P = imp.x * pp + imp.z * ax;
            float LA = imp.x * ss1 + imp.y + imp.z * aa1, LB = imp.x * ss2 + imp.y + imp.z * aa2;
            A.SC -= mA * P; A.SA -= iA * LA; B.SC += mB * P; B.SA += iB * LB;
            return linearError <= B2.LinearSlop && angularError <= B2.AngularSlop;
        }

        public override Vector2 ReactionForce(float invDt) => invDt * (impulse.x * perp + (motorImpulse + lowerImpulse - upperImpulse) * axis);
        public override float ReactionTorque(float invDt) => invDt * impulse.y;
    }

    // ------------------------------------------------------------------------------------------- wheel (WheelJoint2D)
    public sealed class WheelJoint2 : AnchoredJoint2
    {
        public Vector2 LocalXAxisA = Vector2.up;    // suspension axis
        public bool EnableMotor, EnableLimit; public float MotorSpeed, MaxMotorTorque, Stiffness, Damping, Lower, Upper;
        float impulse, motorImpulse, springImpulse, lowerImpulse, upperImpulse, translation;
        Vector2 ax, ay; float sAx, sBx, sAy, sBy, mass, motorMass, axialMass, springMass, bias, gamma;
        Vector2 LocalYAxisA => B2.Cross(1f, LocalXAxisA);
        public float MotorImpulse => motorImpulse;
        public float JointTranslation => B2.Dot(A.Xf.q.Mul(LocalXAxisA), WorldAnchorB - WorldAnchorA);
        public float JointLinearSpeed
        {
            get
            {
                var ra = A.Xf.q.Mul(LocalAnchorA - A.LocalCenter); var rb = B.Xf.q.Mul(LocalAnchorB - B.LocalCenter);
                var d = B.C + rb - A.C - ra; var axis = A.Xf.q.Mul(LocalXAxisA);
                return B2.Dot(d, B2.Cross(A.W, axis)) + B2.Dot(axis, B.V + B2.Cross(B.W, rb) - A.V - B2.Cross(A.W, ra));
            }
        }
        public float JointAngle => B.A - A.A;
        public float JointAngularSpeed => B.W - A.W;

        void Apply(Vector2 P, float LA, float LB) { A.SV -= mA * P; A.SW -= iA * LA; B.SV += mB * P; B.SW += iB * LB; }

        public override void InitVelocity(in TimeStep2 step)
        {
            Prepare();
            var qA = new Rot(A.SA);
            var d = B.SC + rB - A.SC - rA;
            ay = qA.Mul(LocalYAxisA); sAy = B2.Cross(d + rA, ay); sBy = B2.Cross(rB, ay);
            mass = mA + mB + iA * sAy * sAy + iB * sBy * sBy; if (mass > 0) mass = 1 / mass;
            ax = qA.Mul(LocalXAxisA); sAx = B2.Cross(d + rA, ax); sBx = B2.Cross(rB, ax);
            float invMass = mA + mB + iA * sAx * sAx + iB * sBx * sBx;
            axialMass = invMass > 0 ? 1 / invMass : 0;
            springMass = 0; bias = 0; gamma = 0;
            if (Stiffness > 0 && invMass > 0)
            {
                float C = B2.Dot(d, ax), h = step.Dt;
                gamma = h * (Damping + h * Stiffness); if (gamma > 0) gamma = 1 / gamma;
                bias = C * h * Stiffness * gamma;
                springMass = invMass + gamma; if (springMass > 0) springMass = 1 / springMass;
            }
            else springImpulse = 0;
            if (EnableLimit) translation = B2.Dot(ax, d); else lowerImpulse = upperImpulse = 0;
            if (EnableMotor) { motorMass = iA + iB; if (motorMass > 0) motorMass = 1 / motorMass; }
            else { motorMass = 0; motorImpulse = 0; }
            if (step.WarmStarting)
            {
                impulse *= step.DtRatio; springImpulse *= step.DtRatio; motorImpulse *= step.DtRatio;
                float axial = springImpulse + lowerImpulse - upperImpulse;
                Apply(impulse * ay + axial * ax, impulse * sAy + axial * sAx + motorImpulse, impulse * sBy + axial * sBx + motorImpulse);
            }
            else impulse = springImpulse = motorImpulse = lowerImpulse = upperImpulse = 0;
        }

        float AxCdot() => B2.Dot(ax, B.SV - A.SV) + sBx * B.SW - sAx * A.SW;

        public override void SolveVelocity(in TimeStep2 step)
        {
            {
                float imp = -springMass * (AxCdot() + bias + gamma * springImpulse);
                springImpulse += imp; Apply(imp * ax, imp * sAx, imp * sBx);
            }
            {
                float Cdot = B.SW - A.SW - MotorSpeed;
                float imp = -motorMass * Cdot, old = motorImpulse, max = step.Dt * MaxMotorTorque;
                motorImpulse = Math.Clamp(motorImpulse + imp, -max, max); imp = motorImpulse - old;
                A.SW -= iA * imp; B.SW += iB * imp;
            }
            if (EnableLimit)
            {
                {
                    float C = translation - Lower;
                    float imp = -axialMass * (AxCdot() + Math.Max(C, 0) * step.InvDt), old = lowerImpulse;
                    lowerImpulse = Math.Max(lowerImpulse + imp, 0); imp = lowerImpulse - old;
                    Apply(imp * ax, imp * sAx, imp * sBx);
                }
                {
                    float C = Upper - translation;
                    float imp = -axialMass * (-AxCdot() + Math.Max(C, 0) * step.InvDt), old = upperImpulse;
                    upperImpulse = Math.Max(upperImpulse + imp, 0); imp = upperImpulse - old;
                    Apply(-imp * ax, -imp * sAx, -imp * sBx);
                }
            }
            {
                float Cdot = B2.Dot(ay, B.SV - A.SV) + sBy * B.SW - sAy * A.SW;
                float imp = -mass * Cdot;
                impulse += imp; Apply(imp * ay, imp * sAy, imp * sBy);
            }
        }

        public override bool SolvePosition(in TimeStep2 step)
        {
            var qA = new Rot(A.SA); var qB = new Rot(B.SA);
            var ra = qA.Mul(LocalAnchorA - lcA); var rb = qB.Mul(LocalAnchorB - lcB);
            var d = B.SC - A.SC + rb - ra;
            var yax = qA.Mul(LocalYAxisA);
            float say = B2.Cross(d + ra, yax), sby = B2.Cross(rb, yax);
            float C = B2.Dot(d, yax);
            float invMass = mA + mB + iA * sAy * sAy + iB * sBy * sBy;
            float imp = invMass != 0 ? -C / invMass : 0;
            A.SC -= mA * imp * yax; A.SA -= iA * imp * say;
            B.SC += mB * imp * yax; B.SA += iB * imp * sby;
            return Math.Abs(C) <= B2.LinearSlop;
        }

        public override Vector2 ReactionForce(float invDt) => invDt * (impulse * ay + (springImpulse + lowerImpulse - upperImpulse) * ax);
        public override float ReactionTorque(float invDt) => invDt * motorImpulse;
    }

    // ------------------------------------------------------------------------------------------- friction (FrictionJoint2D)
    public sealed class FrictionJoint2 : AnchoredJoint2
    {
        public float MaxForce, MaxTorque;
        Vector2 linearImpulse; float angularImpulse, angularMass; Mat22 linearMass;

        public override void InitVelocity(in TimeStep2 step)
        {
            Prepare();
            var K = new Mat22 { a11 = mA + mB + iA * rA.y * rA.y + iB * rB.y * rB.y, a21 = -iA * rA.x * rA.y - iB * rB.x * rB.y, a22 = mA + mB + iA * rA.x * rA.x + iB * rB.x * rB.x };
            K.a12 = K.a21; linearMass = K.Inverse();
            angularMass = iA + iB; if (angularMass > 0) angularMass = 1 / angularMass;
            if (step.WarmStarting) { linearImpulse *= step.DtRatio; angularImpulse *= step.DtRatio; ApplyLinear(linearImpulse, angularImpulse, angularImpulse); }
            else { linearImpulse = Vector2.zero; angularImpulse = 0; }
        }

        public override void SolveVelocity(in TimeStep2 step)
        {
            float h = step.Dt;
            {
                float imp = -angularMass * (B.SW - A.SW), old = angularImpulse, max = h * MaxTorque;
                angularImpulse = Math.Clamp(angularImpulse + imp, -max, max); imp = angularImpulse - old;
                A.SW -= iA * imp; B.SW += iB * imp;
            }
            {
                var Cdot = B.SV + B2.Cross(B.SW, rB) - A.SV - B2.Cross(A.SW, rA);
                var imp = -linearMass.Mul(Cdot); var old = linearImpulse;
                linearImpulse += imp;
                float max = h * MaxForce;
                if (linearImpulse.sqrMagnitude > max * max) linearImpulse = linearImpulse.normalized * max;
                ApplyLinear(linearImpulse - old);
            }
        }

        public override bool SolvePosition(in TimeStep2 step) => true;
        public override Vector2 ReactionForce(float invDt) => invDt * linearImpulse;
        public override float ReactionTorque(float invDt) => invDt * angularImpulse;
    }

    // ------------------------------------------------------------------------------------------- motor (RelativeJoint2D)
    public sealed class MotorJoint2 : Joint2
    {
        public Vector2 LinearOffset; public float AngularOffset, MaxForce = 1, MaxTorque = 1, CorrectionFactor = 0.3f;
        Vector2 linearImpulse, rA, rB, linearError; float angularImpulse, angularMass, angularError, mA, mB, iA, iB; Mat22 linearMass;

        public override void InitVelocity(in TimeStep2 step)
        {
            mA = A.InvMass; mB = B.InvMass; iA = A.InvI; iB = B.InvI;
            rA = new Rot(A.SA).Mul(LinearOffset - A.LocalCenter); rB = new Rot(B.SA).Mul(-B.LocalCenter);
            var K = new Mat22 { a11 = mA + mB + iA * rA.y * rA.y + iB * rB.y * rB.y, a21 = -iA * rA.x * rA.y - iB * rB.x * rB.y, a22 = mA + mB + iA * rA.x * rA.x + iB * rB.x * rB.x };
            K.a12 = K.a21; linearMass = K.Inverse();
            angularMass = iA + iB; if (angularMass > 0) angularMass = 1 / angularMass;
            linearError = B.SC + rB - A.SC - rA;
            angularError = B.SA - A.SA - AngularOffset;
            if (step.WarmStarting)
            {
                linearImpulse *= step.DtRatio; angularImpulse *= step.DtRatio;
                A.SV -= mA * linearImpulse; A.SW -= iA * (B2.Cross(rA, linearImpulse) + angularImpulse);
                B.SV += mB * linearImpulse; B.SW += iB * (B2.Cross(rB, linearImpulse) + angularImpulse);
            }
            else { linearImpulse = Vector2.zero; angularImpulse = 0; }
        }

        public override void SolveVelocity(in TimeStep2 step)
        {
            float h = step.Dt, invH = step.InvDt;
            {
                float Cdot = B.SW - A.SW + invH * CorrectionFactor * angularError;
                float imp = -angularMass * Cdot, old = angularImpulse, max = h * MaxTorque;
                angularImpulse = Math.Clamp(angularImpulse + imp, -max, max); imp = angularImpulse - old;
                A.SW -= iA * imp; B.SW += iB * imp;
            }
            {
                var Cdot = B.SV + B2.Cross(B.SW, rB) - A.SV - B2.Cross(A.SW, rA) + invH * CorrectionFactor * linearError;
                var imp = -linearMass.Mul(Cdot); var old = linearImpulse;
                linearImpulse += imp;
                float max = h * MaxForce;
                if (linearImpulse.sqrMagnitude > max * max) linearImpulse = linearImpulse.normalized * max;
                imp = linearImpulse - old;
                A.SV -= mA * imp; A.SW -= iA * B2.Cross(rA, imp);
                B.SV += mB * imp; B.SW += iB * B2.Cross(rB, imp);
            }
        }

        public override bool SolvePosition(in TimeStep2 step) => true;
        public override Vector2 ReactionForce(float invDt) => invDt * linearImpulse;
        public override float ReactionTorque(float invDt) => invDt * angularImpulse;
    }
}
