Shader "Custom/ApplyBinaryMask"
{
    Properties
    {
        _MainTex ("Input", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex;
            float _Threshold;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float mask = tex2D(_MaskTex, i.uv).r;
                mask = mask >= _Threshold ? 1.0 : 0.0;
                fixed4 c = tex2D(_MainTex, i.uv);
                return fixed4(c.rgb * mask, c.a);
            }
            ENDCG
        }
    }
}
