Shader "Unlit/EnvironmentDepthToTransparencyMeters"
{
    Properties
    {
        _UseStereo ("Use Stereo", Float) = 1
        _EyeIndex ("Eye Index", Range(0, 1)) = 0
        _FlipV ("Flip V", Float) = 0
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

                float d = UNITY_SAMPLE_TEX2DARRAY(_EnvironmentDepthTexture, float3(duv, eye)).r;

                float z_ndc = d * 2.0 - 1.0;
                float meters = 1.0 / (z_ndc + _EnvironmentDepthZBufferParams.y) * _EnvironmentDepthZBufferParams.x;

                bool inRange = (meters >= _DepthMinMeters) && (meters <= _DepthMaxMeters);
                float alpha = inRange ? 0.0 : 1.0;

                return float4(0.0, 0.0, 0.0, alpha);
            }
            ENDCG
        }
    }
}
