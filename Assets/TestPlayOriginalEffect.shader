Shader "WindomXP/TestPlayOriginalEffect"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _TrailUV ("Trail UV", Float) = 0
        _TintColor ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 1
    }

    // The current 6000.6 project resolves the built-in renderer and does not
    // declare a Universal Render Pipeline package. Keep this effect independent
    // of an unavailable URP include; the additive texture semantics are shared
    // by the standalone Player and Editor fallback renderer.
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "OriginalEffectBuiltIn"
            Blend SrcAlpha [_DstBlend]
            ZTest LEqual
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TintColor;
            float _TrailUV;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                // Original CLaser: U across width, V from newest to oldest node.
                float2 uv = lerp(input.uv, float2(input.uv.y, 1-input.uv.x), _TrailUV);
                output.uv = TRANSFORM_TEX(uv, _MainTex);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                return tex2D(_MainTex, input.uv) * _TintColor;
            }
            ENDCG
        }
    }
}
