// ============================================================================
// FondoMovU.shader — Sin luz, con el color de cada vértice
// ============================================================================
// Lo usa EntornoCampus (cerros, ciudad, cielo, nubes). El color de cada
// vértice ya trae la sombra de la cara y la bruma de la distancia horneadas,
// así que el fondo no depende de la luz ni de la niebla de la escena y no
// cambia el aspecto de los interiores.
//
// Vive en Resources/ a propósito: Shader.Find solo encuentra en el ejecutable
// los shaders que el build incluye, y todo lo que está en Resources/ entra.
// ============================================================================

Shader "MovU/FondoUnlit"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Fondo"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Los colores se pintan en sRGB; el proyecto trabaja en lineal.
                o.color = pow(saturate(input.color.rgb), 2.2);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(input.color, 1.0);
            }
            ENDHLSL
        }
    }
}
