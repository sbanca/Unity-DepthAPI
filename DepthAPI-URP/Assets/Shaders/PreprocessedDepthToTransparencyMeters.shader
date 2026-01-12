Shader "Unlit/EnvironmentDepthToTransparencyMeters"
{
    Properties
    {
        _UseStereo ("Use Stereo", Float) = 1
        _EyeIndex ("Eye Index", Range(0, 1)) = 0
        _FlipV ("Flip V", Float) = 0
        _RadiusUV ("Circle Radius (UV)", Range(0, 0.75)) = 0.5
        _Score ("Score", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Assets/Shaders/Includes/DepthRangeGlobals.hlsl"
            #include "Assets/Shaders/Includes/PlaneGlobals.hlsl"

            UNITY_DECLARE_TEX2DARRAY(_EnvironmentDepthTexture);
            float4x4 _EnvironmentDepthReprojectionMatrices[2];
            float4 _EnvironmentDepthZBufferParams;
            float _UseStereo;
            float _EyeIndex;
            float _FlipV;
            float _RadiusUV;
            float _Score;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                return o;
            }

            float3 RampColor(float score)
            {
                float3 c0 = float3(0.1843, 0.5020, 0.9294);
                float3 c1 = float3(0.9490, 0.7882, 0.2980);
                float3 c2 = float3(0.4353, 0.8118, 0.5922);
                float3 c3 = float3(0.1529, 0.6824, 0.3765);

                if (score < 0.4)
                {
                    float t = score / 0.4;
                    return lerp(c0, c1, t);
                }
                if (score < 0.7)
                {
                    float t = (score - 0.4) / 0.3;
                    return lerp(c1, c2, t);
                }
                float t = (score - 0.7) / 0.3;
                return lerp(c2, c3, t);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                int eye = (_UseStereo > 0.5) ? unity_StereoEyeIndex : (int)_EyeIndex;
                float2 uv = i.uv;
                if (_FlipV > 0.5)
                {
                    uv.y = 1.0 - uv.y;
                }
                float2 uvN = uv * 2.0 - 1.0;

                float3 worldPos = _PlaneCenterWS
                                + uvN.x * _PlaneRightHalfWS
                                + uvN.y * _PlaneUpHalfWS;

                float4 clip = mul(_EnvironmentDepthReprojectionMatrices[eye], float4(worldPos, 1.0));
                if (clip.w <= 0) return float4(0.0, 0.0, 0.0, 1.0);

                float2 duv = clip.xy / clip.w * 0.5 + 0.5;
                if (duv.x < 0 || duv.x > 1 || duv.y < 0 || duv.y > 1) return float4(0.0, 0.0, 0.0, 1.0);

                float4 clipCenter = mul(_EnvironmentDepthReprojectionMatrices[eye], float4(_PlaneCenterWS, 1.0));
                if (clipCenter.w <= 0) return float4(0.0, 0.0, 0.0, 1.0);
                float2 centerDuv = clipCenter.xy / clipCenter.w * 0.5 + 0.5;
                float2 centered = duv - centerDuv;
                if (length(centered) > _RadiusUV)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                float d = UNITY_SAMPLE_TEX2DARRAY(_EnvironmentDepthTexture, float3(duv, eye)).r;

                float z_ndc = d * 2.0 - 1.0;
                float meters = 1.0 / (z_ndc + _EnvironmentDepthZBufferParams.y) * _EnvironmentDepthZBufferParams.x;

                bool inRange = (meters >= _DepthMinMeters) && (meters <= _DepthMaxMeters);
                float alpha = inRange ? 0.0 : 1.0;
                float3 color = RampColor(saturate(_Score));

                return float4(color, alpha);
            }
            ENDCG
        }
    }
}
