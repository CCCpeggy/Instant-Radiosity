﻿using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// the output color tutorial.
/// </summary>
public class Test : RayTracingTutorial
{
  enum Status{
    GenVLP,
    GetVLPFromGPU,
    GenScene,
    None
  }
  private Status status = Status.GenVLP;
  struct VLP {
    public Vector3 pos;
    public float intensity;
  }
  private readonly int _PRNGStatesShaderId = Shader.PropertyToID("_PRNGStates");

  /// <summary>
  /// the frame index.
  /// </summary>
  private int _frameIndex = 0;
  public const int VPLLoopCount = 1;
  public const int SamplingCountOneSide = 300;
  private readonly int _frameIndexShaderId = Shader.PropertyToID("_FrameIndex");
  private readonly int _lightSamplePosBufferId = Shader.PropertyToID("_LightSamplePosBuffer");
  private readonly int _vitualLightPointsId = Shader.PropertyToID("_VitualLightPoints");
  private readonly int _samplingCountOneSideId = Shader.PropertyToID("_SamplingCountOneSide");

  public List<Vector3> LightSamplePos;

  private VLP[] vlp;
  TestAsset asset;
  private ComputeBuffer VitualLightPointsBuffer;
  /// <summary>
  /// constructor.
  /// </summary>
  /// <param name="asset">the tutorial asset.</param>
  public Test(TestAsset asset) : base(asset)
  {
    this.asset = asset;
    LightSamplePos = asset.LightSamplePos;
  }

  public override void Render(ScriptableRenderContext context, Camera camera)
  {
    base.Render(context, camera);
    var outputTarget = RequireOutputTarget(camera);
    // var outputTargetSize = RequireOutputTargetSize(camera);

    // var accelerationStructure = _pipeline.RequestAccelerationStructure();
    // var PRNGStates = _pipeline.RequirePRNGStates(camera);
    // var lightSamplePosBuffer = ((mRayTracingRenderPipeline)_pipeline).RequireComputeBuffer(_lightSamplePosBufferId, LightSamplePos);
    var cmd = CommandBufferPool.Get(typeof(OutputColorTutorial).Name);
    
    try {
      switch (status) {
        case Status.GenVLP:
          GenerateVPL(cmd, context, camera);
          status = Status.GetVLPFromGPU;
          break;
        case Status.GetVLPFromGPU:

          int vlpLen = LightSamplePos.Count * VPLLoopCount * 10;
          vlp = new VLP[vlpLen];
          VitualLightPointsBuffer.GetData(vlp);
          vlp = vlp.Where(x=>x.intensity > 0).ToArray();

          DrawLightPoint(cmd, context, camera);
          
          status = Status.None;
          break;
        case Status.GenScene:
          GenerateScene(cmd, context, camera);
          break;
        case Status.None:
          break;
      }
    }
    finally
    {
      context.Submit();
      CommandBufferPool.Release(cmd);
    }
  }

  private void DrawLightPoint(CommandBuffer cmd, ScriptableRenderContext context, Camera camera) {
    cmd.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0));
    context.ExecuteCommandBuffer(cmd);
    cmd.Clear();

    using (new ProfilingSample(cmd, "RenderScene")) {
      context.SetupCameraProperties(camera);
      context.DrawSkybox(camera);
      foreach (var renderer in SceneManager.Instance.renderers) {
        cmd.DrawRenderer(renderer, renderer.sharedMaterial, 0, 0);
      }
      foreach (var v in vlp) {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = v.pos;
        sphere.transform.localScale = new Vector3(1, 1, 1) * 0.05f;
        Renderer renderer = sphere.GetComponent<Renderer>();
        cmd.DrawRenderer(renderer, asset.LightMaterial);
      }
      context.ExecuteCommandBuffer(cmd);
    }
  }

  private void GenerateVPL(CommandBuffer cmd, ScriptableRenderContext context, Camera camera) {
    int vlpLen = LightSamplePos.Count * VPLLoopCount * 10;
    VitualLightPointsBuffer = new ComputeBuffer(vlpLen, sizeof(float) * 4);
    var accelerationStructure = _pipeline.RequestAccelerationStructure();
    var PRNGStates = _pipeline.RequirePRNGStates(camera);
    var lightSamplePosBuffer = ((mRayTracingRenderPipeline)_pipeline).RequireComputeBuffer(_lightSamplePosBufferId, LightSamplePos);
    using (new ProfilingSample(cmd, "RayTracing")) {
      cmd.SetRayTracingShaderPass(_shader, "RayTracing");
      cmd.SetRayTracingAccelerationStructure(
        _shader, _pipeline.accelerationStructureShaderId, accelerationStructure);
      cmd.SetRayTracingBufferParam(_shader, _PRNGStatesShaderId, PRNGStates);
      cmd.SetRayTracingBufferParam(_shader, _vitualLightPointsId, VitualLightPointsBuffer);
      cmd.SetGlobalBuffer(_lightSamplePosBufferId, lightSamplePosBuffer);
      cmd.DispatchRays(_shader, "PutLightSource", (uint)LightSamplePos.Count, VPLLoopCount, 1, camera);

      context.ExecuteCommandBuffer(cmd);
    }
  }
 
  private void GenerateScene(CommandBuffer cmd, ScriptableRenderContext context, Camera camera, int vlpIdx=-1) {
    //   if (_frameIndex < SamplingCountOneSide * SamplingCountOneSide)
    //   {
    //     using (new ProfilingSample(cmd, "RayTracing"))
    //     {
    //       cmd.SetRayTracingShaderPass(_shader, "RayTracing");
    //       cmd.SetRayTracingAccelerationStructure(_shader, _pipeline.accelerationStructureShaderId,
    //         accelerationStructure);
    //       cmd.SetRayTracingIntParam(_shader, _frameIndexShaderId, _frameIndex);
    //       cmd.SetRayTracingIntParam(_shader, _samplingCountOneSideId, SamplingCountOneSide);
    //       cmd.SetRayTracingBufferParam(_shader, _PRNGStatesShaderId, PRNGStates);
    //       cmd.SetRayTracingTextureParam(_shader, _outputTargetShaderId, outputTarget);
    //       cmd.SetRayTracingVectorParam(_shader, _outputTargetSizeShaderId, outputTargetSize);
    //       cmd.SetGlobalBuffer(_lightSamplePosBufferId, lightSamplePosBuffer);
    //       cmd.DispatchRays(_shader, "AntialiasingRayGenShader", (uint) outputTarget.rt.width,
    //         (uint) outputTarget.rt.height, 1, camera);
    //     }

    //     context.ExecuteCommandBuffer(cmd);
    //     if (camera.cameraType == CameraType.Game)
    //       _frameIndex++;

    //     using (new ProfilingSample(cmd, "FinalBlit"))
    //     {
    //       cmd.Blit(outputTarget, BuiltinRenderTextureType.CameraTarget, Vector2.one, Vector2.zero);

    //       if (false && _frameIndex % 100 == 0) {
    //         // RenderTexture outputRenderTexture = RenderTexture.active;
    //         // var scale = RTHandles.rtHandleProperties.rtHandleScale;
    //         // cmd.Blit(outputTarget, outputRenderTexture, new Vector2(scale.x, scale.y), Vector2.zero, 0, 0);

    //         Texture2D tex = new Texture2D(camera.pixelWidth, camera.pixelHeight, TextureFormat.RGB24, false);
    //         // // ReadPixels looks at the active RenderTexture.
    //         // RenderTexture.active = outputRenderTexture;
    //         tex.ReadPixels(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight), 0, 0);
    //         tex.Apply();

    //         string path = Application.dataPath + "/" + _frameIndex + ".png";
    //         Debug.Log(path);
    //         File.WriteAllBytes(path, tex.EncodeToPNG());
    //       }
    //     }
    //   }

    //   context.ExecuteCommandBuffer(cmd);
  }
}
