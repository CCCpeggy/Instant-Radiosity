Shader "Test/Diffuse"
{
  Properties
  {
    _Color ("Main Color", Color) = (1, 1, 1, 1)
    _Reflect ("Reflect", float) = 5
    _Diffuse ("Diffuse", float) = 0.5
  }
  SubShader
  {
    Tags { "RenderType"="Opaque" }
    LOD 100

    Pass
    {
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      // make fog work
      #pragma multi_compile_fog

      #include "UnityCG.cginc"

      struct appdata
      {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
      };

      struct v2f
      {
        float3 normal : TEXCOORD0;
        UNITY_FOG_COORDS(1)
        float4 vertex : SV_POSITION;
      };

      CBUFFER_START(UnityPerMaterial)
      half4 _Color;
      CBUFFER_END

      v2f vert (appdata v)
      {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.normal = UnityObjectToWorldNormal(v.normal);
        UNITY_TRANSFER_FOG(o, o.vertex);
        return o;
      }

      half4 frag (v2f i) : SV_Target
      {
        half4 col = _Color * half4(dot(i.normal, float3(0.0f, 1.0f, 0.0f)).xxx, 1.0f);
        // apply fog
        UNITY_APPLY_FOG(i.fogCoord, col);
        return col;
      }
      ENDCG
    }
  }

  SubShader
  {
    Pass
    {
      Name "RayTracing"
      Tags { "LightMode" = "RayTracing" }

      HLSLPROGRAM

      #pragma raytracing test

      #include "./Common.hlsl"
      #include "../GPU-Ray-Tracing-in-One-Weekend/src/Assets/Shaders/PRNG.hlsl"

      struct IntersectionVertex
      {
        // Object space normal of the vertex
        float3 normalOS;
        float2 uv0;
      };

      CBUFFER_START(UnityPerMaterial)
      float4 _Color;
      float _Reflect;
      float _Diffuse;
      CBUFFER_END

      void FetchIntersectionVertex(uint vertexIndex, out IntersectionVertex outVertex)
      {
        outVertex.normalOS = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
        outVertex.uv0 = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeTexCoord0);
      }

      float3 GetRandomOnSphereByPow(inout uint4 states, float _pow)
      {
        float r1 = GetRandomValue(states);
        float r2 = GetRandomValue(states);
        r1 = pow(r1, _pow);
        r2 = pow(r2, _pow);
        float x = cos(2.0f * (float)M_PI * r1) * 2.0f * sqrt(r2 * (1.0f - r2));
        float y = sin(2.0f * (float)M_PI * r1) * 2.0f * sqrt(r2 * (1.0f - r2));
        float z = 1.0f - 2.0f * r2;
        return float3(x / _pow, y, z / _pow);
      }

      [shader("closesthit")]
      void ClosestHitShader(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
      {
        // Fetch the indices of the currentr triangle
        uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

        // Fetch the 3 vertices
        IntersectionVertex v0, v1, v2;
        FetchIntersectionVertex(triangleIndices.x, v0);
        FetchIntersectionVertex(triangleIndices.y, v1);
        FetchIntersectionVertex(triangleIndices.z, v2);

        // Compute the full barycentric coordinates
        float3 barycentricCoordinates = float3(1.0 - attributeData.barycentrics.x - attributeData.barycentrics.y, attributeData.barycentrics.x, attributeData.barycentrics.y);

        // Get normal in world space.
        float3 normalOS = INTERPOLATE_RAYTRACING_ATTRIBUTE(v0.normalOS, v1.normalOS, v2.normalOS, barycentricCoordinates);
        float3x3 objectToWorld = (float3x3)ObjectToWorld3x4();
        float3 normalWS = normalize(mul(objectToWorld, normalOS));

        float4 subColor = float4(0, 0, 0, 1);
        // Get next color
        if (rayIntersection.remainingDepth > 1)
        {
          // Get position in world space.
          float3 origin = WorldRayOrigin();
          float3 direction = WorldRayDirection();
          float t = RayTCurrent();
          float3 positionWS = origin + direction * t;

          // Make reflection ray.
          RayDesc rayDescriptor;
          rayDescriptor.Origin = positionWS + 0.001f * normalWS;
          bool isSpeculiar = false;
          if (GetRandomValue(rayIntersection.PRNGStates) < _Diffuse) {
            rayDescriptor.Direction = normalize(normalWS + GetRandomOnUnitSphere(rayIntersection.PRNGStates));
          } else {
            isSpeculiar = true;
            rayDescriptor.Direction = reflect(-positionWS, normalWS) + GetRandomOnSphereByPow(rayIntersection.PRNGStates, _Reflect);
          }
          rayDescriptor.TMin = 1e-5f;
          rayDescriptor.TMax = _CameraFarDistance;

          // Tracing reflection.
          RayIntersection reflectionRayIntersection;
          reflectionRayIntersection.remainingDepth = rayIntersection.remainingDepth - 1;
          reflectionRayIntersection.PRNGStates = rayIntersection.PRNGStates;
          reflectionRayIntersection.color = float4(0.0f, 0.0f, 0.0f, 0.0f);
          reflectionRayIntersection.type = 0;

          TraceRay(_AccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, 0xFF, 0, 1, 0, rayDescriptor, reflectionRayIntersection);

          rayIntersection.PRNGStates = reflectionRayIntersection.PRNGStates;
          float r = reflectionRayIntersection.distance;
          if (r < 1) r = 1;
          subColor = reflectionRayIntersection.color / (r * r);
          // if(isSpeculiar && _Diffuse > 0) subColor *= 1 + _Diffuse;
          subColor.a = 1.0;
        }

        float4 lightColor = float4(0, 0, 0, 1);
        // shadow ray
        if (rayIntersection.remainingDepth > 0) {
          float3 origin = WorldRayOrigin();
          float3 direction = WorldRayDirection();
          float t = RayTCurrent();
          float3 positionWS = origin + direction * t;

          uint numStructs;
          _LightSamplePosBuffer.GetDimensions(numStructs);

          // Make reflection ray.
          RayDesc rayDescriptor;
          rayDescriptor.Origin = positionWS + 0.001f * normalWS;
          rayDescriptor.TMin = 1e-5f;
          rayDescriptor.TMax = _CameraFarDistance;
    


          uint lightIdx = GetRandomValue(rayIntersection.PRNGStates) * numStructs;
          // Tracing reflection.
          RayIntersection shadowRayIntersection;
          shadowRayIntersection.PRNGStates = rayIntersection.PRNGStates;
          shadowRayIntersection.remainingDepth = rayIntersection.remainingDepth - 1;
          shadowRayIntersection.color = float4(0.0f, 0.0f, 0.0f, 0.0f);
          shadowRayIntersection.type = 0;

          float3 lightPos = _LightSamplePosBuffer[lightIdx];
          rayDescriptor.Direction = lightPos - positionWS;

          TraceRay(_AccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, 0xFF, 0, 1, 0, rayDescriptor, shadowRayIntersection);
          if (shadowRayIntersection.type == 1) {
            float r = shadowRayIntersection.distance;
            // if (r < 1) r = 1;
            lightColor = shadowRayIntersection.color / (r * r);
            // if (lightColor.x > 1.0 && lightColor.y > 1.0 && lightColor.z > 1.0) break;
          }
          
          lightColor.a = 1.0;
          // lightColor *= lightRate(lightPos, normalWS);
          rayIntersection.PRNGStates = shadowRayIntersection.PRNGStates;
        }
        
        float2 uv0 = INTERPOLATE_RAYTRACING_ATTRIBUTE(v0.uv0, v1.uv0, v2.uv0, barycentricCoordinates);

        rayIntersection.color = _Color * lightColor * 0.6f + subColor * 0.4f;
        rayIntersection.type = 0;
        rayIntersection.distance = GetDistance();
      }

      ENDHLSL
    }
  }
}
