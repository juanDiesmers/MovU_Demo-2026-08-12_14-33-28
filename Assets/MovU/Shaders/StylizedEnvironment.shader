// ============================================================================
// StylizedEnvironment.shader  —  MovU
// ============================================================================
// Shader URP que estiliza una malla arquitectonica SIN necesidad de UVs.
//
// El OBJ de Meshy no trae coordenadas de textura (vt: 0), asi que cualquier
// material con textura clasica se veria estirado o directamente gris. Este
// shader resuelve eso proyectando todo desde el espacio de mundo:
//
//   * Clasifica cada pixel en PISO / MURO / TECHO segun la normal de mundo.
//   * El piso dibuja baldosas proceduralmente (rejilla en metros reales,
//     con junta antialiaseada y alternancia de tono entre baldosas).
//   * Los muros llevan un degradado vertical, zocalo en la base y juntas
//     verticales sutiles cada cierto ancho.
//   * Añade oclusion de contacto falsa en el arranque de los muros, que es
//     lo que mas ayuda a leer el volumen de un modelo sin texturas.
//
// Todo se mide en METROS DE MUNDO, asi que el resultado no depende de la
// escala del modelo (el plano va a 28x) ni de que la malla tenga UVs.
// ============================================================================

Shader "MovU/StylizedEnvironment"
{
    Properties
    {
        [Header(Piso)]
        _FloorColor        ("Color de baldosa A", Color)        = (0.64, 0.66, 0.69, 1)
        _FloorColorAlt     ("Color de baldosa B", Color)        = (0.58, 0.60, 0.64, 1)
        _TileSize          ("Tamano de baldosa (m)", Float)     = 1.2
        _GroutColor        ("Color de junta", Color)            = (0.42, 0.44, 0.48, 1)
        _GroutWidth        ("Ancho de junta (m)", Float)        = 0.035

        [Header(Muros)]
        _WallColor         ("Color de muro (abajo)", Color)     = (0.86, 0.86, 0.83, 1)
        _WallTopColor      ("Color de muro (arriba)", Color)    = (0.95, 0.95, 0.93, 1)
        _WallGradientHeight("Altura del degradado (m)", Float)  = 3.0
        _BaseboardColor    ("Color de zocalo", Color)           = (0.28, 0.31, 0.36, 1)
        _BaseboardHeight   ("Altura de zocalo (m)", Float)      = 0.14
        _PanelWidth        ("Ancho de panel de muro (m)", Float)= 2.4
        _PanelStrength     ("Fuerza de la junta vertical", Range(0,1)) = 0.5

        [Header(Techo)]
        _CeilingColor      ("Color de techo", Color)            = (0.93, 0.93, 0.95, 1)
        [HDR] _CeilingEmission ("Emision del techo", Color)      = (0, 0, 0, 1)

        [Header(Mezcla y sombreado)]
        _FloorLevel        ("Nivel del piso (Y de mundo)", Float) = 0.0
        _FloorSpacing      ("Separacion entre pisos (m, 0 = un solo piso)", Float) = 0.0
        _Sharpness         ("Nitidez de la mezcla", Range(1, 32)) = 8
        _AOStrength        ("Fuerza del AO de contacto", Range(0,1)) = 0.45
        _AOHeight          ("Alto del AO de contacto (m)", Float)   = 0.55
        _Smoothness        ("Suavidad", Range(0,1))             = 0.15
        _Metallic          ("Metalico", Range(0,1))             = 0.0

        [Header(Render)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 300

        // --------------------------------------------------------------------
        // Bloque compartido por todos los pases
        // --------------------------------------------------------------------
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _FloorColor;
            float4 _FloorColorAlt;
            float  _TileSize;
            float4 _GroutColor;
            float  _GroutWidth;

            float4 _WallColor;
            float4 _WallTopColor;
            float  _WallGradientHeight;
            float4 _BaseboardColor;
            float  _BaseboardHeight;
            float  _PanelWidth;
            float  _PanelStrength;

            float4 _CeilingColor;
            float4 _CeilingEmission;

            float  _FloorLevel;
            float  _FloorSpacing;
            float  _Sharpness;
            float  _AOStrength;
            float  _AOHeight;
            float  _Smoothness;
            float  _Metallic;
            float  _Cull;
        CBUFFER_END

        // Devuelve 1 sobre la linea y 0 fuera, con antialias por derivadas.
        // 'coord' viene en metros, 'width' es el semiancho de la linea en metros.
        float LineMask(float coord, float width)
        {
            float aa = fwidth(coord) + 1e-5;
            return 1.0 - smoothstep(width - aa, width + aa, coord);
        }

        // Color de superficie estilizado a partir de posicion y normal de mundo.
        // Devuelve el albedo en rgb y la oclusion de contacto en a.
        float4 StylizedSurface(float3 positionWS, float3 normalWS, out float3 emission)
        {
            float up = normalWS.y;

            // Mascaras por orientacion. pow() con _Sharpness hace que solo las
            // caras casi horizontales cuenten como piso o techo; todo lo demas
            // (incluidas las rampas de la escalera) cae del lado de muro de a poco.
            float floorMask = pow(saturate( up), _Sharpness);
            float ceilMask  = pow(saturate(-up), _Sharpness);
            float wallMask  = saturate(1.0 - floorMask - ceilMask);

            // ---- PISO: rejilla de baldosas en coordenadas de mundo ----------
            float  tile     = max(_TileSize, 0.01);
            float2 tileUV   = positionWS.xz / tile;
            float2 cellId   = floor(tileUV);
            float2 inCell   = frac(tileUV);
            // Distancia (en metros) al borde de baldosa mas cercano.
            float2 toEdge   = min(inCell, 1.0 - inCell) * tile;
            float  edgeDist = min(toEdge.x, toEdge.y);
            float  grout    = LineMask(edgeDist, max(_GroutWidth, 0.0));

            // Tablero de ajedrez suave: rompe la monotonia sin ruido de textura.
            float  checker  = frac((cellId.x + cellId.y) * 0.5) * 2.0;
            float3 tileCol  = lerp(_FloorColor.rgb, _FloorColorAlt.rgb, checker);
            float3 floorCol = lerp(tileCol, _GroutColor.rgb, grout);

            // ---- MURO: degradado vertical + zocalo + junta de panel ---------
            // Con varios pisos apilados, la altura se mide DENTRO del piso: se
            // toma el resto de dividir por la separacion entre pisos. Sin esto
            // el zocalo y el AO de contacto solo quedarian bien en un piso.
            float  bruta    = positionWS.y - _FloorLevel;
            float  periodo  = max(_FloorSpacing, 0.001);
            float  enPiso   = bruta - floor(bruta / periodo) * periodo;
            float  height   = (_FloorSpacing > 0.01) ? enPiso : bruta;
            float  t        = saturate(height / max(_WallGradientHeight, 0.01));
            float3 wallCol  = lerp(_WallColor.rgb, _WallTopColor.rgb, t);

            float  baseboard = 1.0 - smoothstep(_BaseboardHeight,
                                                _BaseboardHeight + 0.025,
                                                height);
            wallCol = lerp(wallCol, _BaseboardColor.rgb, baseboard);

            // Junta vertical: se recorre el muro por el eje horizontal en el que
            // la superficie realmente varia (si la normal mira en X, avanza en Z).
            float  panelW   = max(_PanelWidth, 0.05);
            float  along    = (abs(normalWS.x) > abs(normalWS.z)) ? positionWS.z : positionWS.x;
            float  inPanel  = frac(along / panelW);
            float  panelD   = min(inPanel, 1.0 - inPanel) * panelW;
            float  seam     = LineMask(panelD, 0.008);
            wallCol *= lerp(1.0, 0.90, seam * _PanelStrength);

            // ---- Combinacion -----------------------------------------------
            float3 albedo = floorCol   * floorMask
                          + wallCol    * wallMask
                          + _CeilingColor.rgb * ceilMask;

            // Oclusion de contacto: oscurece el arranque del muro contra el piso.
            float ao = lerp(1.0 - _AOStrength, 1.0,
                            saturate(height / max(_AOHeight, 0.01)));
            ao = lerp(1.0, ao, wallMask);

            // Con techo cerrado no entra luz del sol: el techo emite un poco y
            // hace de luminaria. Es mucho mas barato que sembrar luces reales.
            emission = _CeilingEmission.rgb * ceilMask;

            return float4(albedo, ao);
        }
        ENDHLSL

        // --------------------------------------------------------------------
        // Pase principal iluminado
        // --------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   LitVert
            #pragma fragment LitFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LitVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.fogCoord   = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 LitFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 N = normalize(IN.normalWS);
                float3 emision;
                float4 surf = StylizedSurface(IN.positionWS, N, emision);

                InputData inputData = (InputData)0;
                inputData.positionWS               = IN.positionWS;
                inputData.normalWS                 = N;
                inputData.viewDirectionWS          = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord              = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord                 = IN.fogCoord;
                inputData.vertexLighting           = half3(0, 0, 0);
                inputData.bakedGI                  = SampleSH(N);
                inputData.normalizedScreenSpaceUV  = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask               = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo             = surf.rgb;
                surfaceData.specular           = half3(0, 0, 0);
                surfaceData.metallic           = _Metallic;
                surfaceData.smoothness         = _Smoothness;
                surfaceData.normalTS           = half3(0, 0, 1);
                surfaceData.emission           = emision;
                surfaceData.occlusion          = surf.a;
                surfaceData.alpha              = 1.0;
                surfaceData.clearCoatMask      = 0.0;
                surfaceData.clearCoatSmoothness= 0.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a   = 1.0;
                return color;
            }
            ENDHLSL
        }

        // --------------------------------------------------------------------
        // Sombras
        // --------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDir));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // --------------------------------------------------------------------
        // Profundidad (prepass, SSAO, niebla de profundidad)
        // --------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // --------------------------------------------------------------------
        // Profundidad + normales (necesario para SSAO y contornos)
        // --------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DNVaryings DepthNormalsVert(DNAttributes IN)
            {
                DNVaryings OUT = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DepthNormalsFrag(DNVaryings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
