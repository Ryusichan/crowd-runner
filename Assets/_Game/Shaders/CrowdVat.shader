// 군중 한 명을 **본 없이** 그린다 — 정점 위치를 구워 둔 텍스처에서 읽는다.
//
// 방식 A(유닛마다 `SkinnedMeshRenderer` + `Animator`)가 S21 에서 **60~70 개체**에 8 ms 예산을
// 다 썼다 (`docs/M0_CROWD.md` §6). 부산 10 판의 최대 병력은 30~150 이라 대부분이 그 위다.
// 그래서 이 셰이더가 **이 군중이 존재하는 방법**이다 — 나중에 할 최적화가 아니다.
//
// 어떻게: 굽는 쪽(`VatBaker`)이 클립을 프레임마다 샘플링해 **정점마다 한 픽셀**씩 위치를 적는다.
// 가로 = 정점, 세로 = 프레임. 그리는 쪽은 `uv2.x`(정점 번호)와 인스턴스마다 다른 `_Phase` 로
// 그 텍스처를 한 번 읽는다. 본도, `Animator` 도, 유닛당 `GameObject` 도 없다.
//
// **본 변환이 아니라 위치를 읽는 것**이라 비용이 본 수와 무관하다 — 방식 A 는 본 20 개를 유닛마다
// 평가했다. 대신 치르는 값: 클립이 **고정**된다 (블렌딩도, 가변 속도도 없다. 위상만 다르다).
// 군중 러너에서 한 명 한 명이 다른 동작을 할 이유가 없으므로 그 대가가 싸다.
Shader "Game/CrowdVat"
{
    Properties
    {
        _Color ("색", Color) = (0.82, 0.78, 0.72, 1)
        _EnemyColor ("적 색", Color) = (0.74, 0.44, 0.44, 1)
        _Vat ("정점 애니메이션 (가로=정점, 세로=프레임)", 2D) = "black" {}
        _Frames ("구운 프레임 수", Float) = 32
        _Verts ("정점 수", Float) = 1500
        _ClipSpeed ("클립 속도 (초당 바퀴)", Float) = 1.25
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // **인스턴싱이 이 셰이더의 전부다.** 이것 없이는 드로우가 유닛마다 하나씩 난다
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _Vat;
            float4 _Color, _EnemyColor;
            float _Frames, _Verts, _ClipSpeed;

            // 인스턴스마다 다른 둘: **걸음 위상**과 **편**(아군/적).
            // 위상이 같으면 1,000 명이 한 몸처럼 움직여 군중으로 안 읽힌다 — `CrowdField.Phase` 가
            // 개체마다 다른 값을 들고 있고, 그 값이 여기로 온다
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Phase)
                UNITY_DEFINE_INSTANCED_PROP(float, _Side)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv2 : TEXCOORD1;     // x = 정점 번호 / (정점 수 - 1)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
                float4 col : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float phase = UNITY_ACCESS_INSTANCED_PROP(Props, _Phase);
                float side = UNITY_ACCESS_INSTANCED_PROP(Props, _Side);

                // 세로 = 프레임. `frac` 으로 돌려서 **루프의 이음매가 없다** (구울 때 첫 프레임과
                // 마지막 프레임을 같게 맞춰 두면 점프가 안 보인다 — `VatBaker` 주석)
                float t = frac(phase + _Time.y * _ClipSpeed);
                // 픽셀 중심을 집는다. 모서리를 집으면 이웃 프레임이 섞여 **몸이 떨린다**
                float row = (floor(t * _Frames) + 0.5) / _Frames;
                float col = (v.uv2.x * (_Verts - 1.0) + 0.5) / _Verts;

                float3 p = tex2Dlod(_Vat, float4(col, row, 0, 0)).xyz;
                o.pos = UnityObjectToClipPos(float4(p, 1.0));
                // 법선은 **원본 메시의 것을 그대로 쓴다.** 프레임마다 굽지 않은 이유: 텍스처가
                // 두 배가 되고, 군중은 화면에서 작아서 그 오차가 안 보인다. 가까이 쓸 일이 생기면
                // 그때 두 번째 텍스처를 굽는다
                o.nrm = UnityObjectToWorldNormal(v.normal);
                o.col = lerp(_Color, _EnemyColor, side);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // 가장 싼 조명 — 위에서 내려오는 한 방향 + 바닥 반사. 군중 러너는 카메라가 멀어서
                // 이보다 더 들여도 화면에서 달라지는 것이 없다 (그리고 1,000 명분이다)
                float3 n = normalize(i.nrm);
                float d = saturate(dot(n, normalize(float3(0.3, 1.0, -0.2)))) * 0.75 + 0.25;
                return fixed4(i.col.rgb * d, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
