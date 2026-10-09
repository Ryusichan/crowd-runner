// 길 표식과 발밑 그림자를 **인스턴싱으로** 그리는 가장 작은 셰이더.
//
// 왜 `Legacy Shaders/Diffuse` 를 안 쓰나: 그쪽은 **인스턴싱 변형이 없다.** 에디터에서는
// 모든 변형이 있어서 `RenderMeshInstanced` 가 그려지지만, **빌드는 쓰이는 변형만 남긴다** —
// 그리고 런타임에 `new Material(...)` + `enableInstancing = true` 로 켠 것은 빌드 시점에
// 아무도 모른다. 그래서 웹 빌드에서 표식과 그림자가 **통째로 안 그려졌다** (2026-10-09).
//
// 군중(`Game/CrowdVat`)이 빌드에서 멀쩡했던 이유가 그 대조군이다: 그쪽 머티리얼은
// `Resources/M0` 의 **진짜 에셋**이라 빌드가 인스턴싱을 쓴다는 것을 안다. 그래서 이 셰이더를
// 쓰는 머티리얼도 **에셋으로 굽는다** (`M0Assets.BakeFlatMaterials`).
//
// 반투명으로 둔 이유: 그림자가 알파를 쓴다. 표식은 알파 1 이라 결과가 불투명과 같다.
Shader "Game/FlatInstanced"
{
    Properties
    {
        _Color ("색", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // **이 한 줄이 전부다.** 없으면 빌드에서 인스턴싱 변형이 안 생기고,
            // `RenderMeshInstanced` 는 조용히 아무것도 안 그린다
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.nrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // 군중 셰이더와 **같은 빛**을 쓴다 — 다르면 같은 장면에서 두 조명이 보인다
                float3 n = normalize(i.nrm);
                float d = saturate(dot(n, normalize(float3(0.3, 1.0, -0.2)))) * 0.75 + 0.25;
                return fixed4(_Color.rgb * d, _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
