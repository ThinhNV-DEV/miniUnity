// VideoRecreate/RoundedBox – bo tròn cạnh & góc của khối hộp bằng vertex shader (thêm 2026-10-10).
// Dùng với mesh "CubeGrid" (cube −0.5..0.5 chia lưới dày ở mép – VideoBuilder.CubeGridMesh()); mesh cube mặc định chỉ có
// 4 đỉnh / mặt nên không có gì để uốn.
// Thuật toán (rounded box): p = vị trí đỉnh đã nhân scale của object; q = clamp(p, −(h−r), h−r); đỉnh mới = q + normalize(p−q)·r,
// normal = normalize(p−q). Bán kính r tính theo ĐƠN VỊ THẾ GIỚI → khối scale không đều vẫn bo đều. "DisableBatching" để giữ scale.
// Surface shader Standard + addshadow → ánh sáng PBR + đổ/nhận bóng đúng hình đã bo.
Shader "VideoRecreate/RoundedBox"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.4
        _Metallic ("Metallic", Range(0, 1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _Radius ("Corner Radius (world units)", Float) = 0.15
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "DisableBatching" = "True" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        fixed4 _Color;
        half _Glossiness, _Metallic;
        half4 _EmissionColor;
        float _Radius;
        fixed4 _RimColor;
        half _RimPower, _RimStrength;

        struct Input
        {
            float rim;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 s = float3(length(unity_ObjectToWorld._m00_m10_m20),
                              length(unity_ObjectToWorld._m01_m11_m21),
                              length(unity_ObjectToWorld._m02_m12_m22));
            float3 p = v.vertex.xyz * s;
            float3 hs = 0.5 * s;
            float r = max(0.0, min(_Radius, min(hs.x, min(hs.y, hs.z))));
            float3 inner = hs - r;
            float3 q = clamp(p, -inner, inner);
            float3 d = p - q;
            float len = length(d);
            if (len > 1e-6 && r > 0)
            {
                float3 n = d / len;
                p = q + n * r;
                v.normal = normalize(n * s);   // normal không gian object (biến đổi ngược của scale)
            }
            v.vertex.xyz = p / s;
            // viền sáng: hướng nhìn và normal cùng trong không gian object (đã bo)
            float3 viewObj = normalize(ObjSpaceViewDir(v.vertex));
            o.rim = pow(1.0 - saturate(dot(viewObj, normalize(v.normal))), _RimPower);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = _EmissionColor.rgb + _RimColor.rgb * IN.rim * _RimStrength;   // viền sáng nhẹ kiểu đồ chơi
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
