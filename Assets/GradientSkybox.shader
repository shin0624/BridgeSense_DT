Shader "Custom/GradientSkybox"
{
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.847, 0.886, 0.902, 1)
        _HorizonColor ("Horizon Color", Color) = (0.969, 0.976, 0.973, 1)
        _GroundColor ("Ground Color", Color) = (0.788, 0.812, 0.788, 1)
        _HorizonHeight ("Horizon Height", Range(-0.3, 0.3)) = 0.0
        _SkyCurve ("Sky Curve", Range(0.3, 6)) = 1.6
        _GroundCurve ("Ground Curve", Range(0.3, 6)) = 1.2
        _HorizonBlend ("Horizon Blend Width", Range(0.01, 0.6)) = 0.12
        _GlowIntensity ("Horizon Glow", Range(0, 2)) = 0.4
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 dir : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
            float4 _ZenithColor;
            float4 _HorizonColor;
            float4 _GroundColor;
            float _HorizonHeight;
            float _SkyCurve;
            float _GroundCurve;
            float _HorizonBlend;
            float _GlowIntensity;
            CBUFFER_END

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float h = dir.y - _HorizonHeight;

                float skyT = pow(saturate(h / _HorizonBlend), 1.0 / _SkyCurve);
                float groundT = pow(saturate(-h / _HorizonBlend), 1.0 / _GroundCurve);

                float4 col = lerp(_HorizonColor, _ZenithColor, skyT);
                col = lerp(col, _GroundColor, groundT);

                float glow = exp(-abs(h) * 8.0) * _GlowIntensity;
                col.rgb += glow * (_HorizonColor.rgb - col.rgb) * 0.5;

                return col;
            }
            ENDHLSL
        }
    }
}
