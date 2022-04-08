using System.IO;
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
    DrawHlslScene,
    GenScene,
    SuggestiveContour,
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
  public const int LoopCountInOneFrame = 30;
  public const int SamplingCountOneSide = 300;
  private readonly int _frameIndexShaderId = Shader.PropertyToID("_FrameIndex");
  private readonly int _lightSamplePosBufferId = Shader.PropertyToID("_LightSamplePosBuffer");
  private readonly int _lightSampleIntensityBufferId = Shader.PropertyToID("_LightSampleIntensityBuffer");
  private readonly int _vitualLightPointsId = Shader.PropertyToID("_VitualLightPoints");
  private readonly int _samplingCountOneSideId = Shader.PropertyToID("_SamplingCountOneSide");
  private readonly int _loopCountInOneFrameId = Shader.PropertyToID("_LoopCountInOneFrame");

  public List<Vector3> LightSamplePos;

  private VLP[] vlp;
  private List<Renderer> vlpRenderer = new List<Renderer>();
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
    var cmd = CommandBufferPool.Get(typeof(OutputColorTutorial).Name);
    bool savePng = false;
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
          foreach (var v in vlp) {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = v.pos;
            sphere.transform.localScale = new Vector3(1, 1, 1) * 0.05f;
            vlpRenderer.Add(sphere.GetComponent<Renderer>());
          };
          status = Status.DrawHlslScene;
          break;
        case Status.DrawHlslScene:
          DrawScene(cmd, context, camera);
          if (camera != Camera.main) status = Status.GenScene;
          break;
        case Status.GenScene:
          if (camera.cameraType != CameraType.Game) return;
          if (camera == Camera.main) return;
          _frameIndex++;
          GenerateScene(cmd, context, camera);
          // status = Status.None;
          // break;
        // case Status.SuggestiveContour:
          // DrawScene(cmd, context, camera);
          int tempRTID = Shader.PropertyToID("_Temp1");
          cmd.GetTemporaryRT(tempRTID, camera.pixelWidth, camera.pixelHeight);
          cmd.Blit(BuiltinRenderTextureType.CameraTarget, tempRTID, Vector2.one, Vector2.zero);
          cmd.SetRenderTarget(new RenderTargetIdentifier(tempRTID));

          cmd.Blit(tempRTID, BuiltinRenderTextureType.CameraTarget, asset.EdgeMaterial);
          context.ExecuteCommandBuffer(cmd);
          
          cmd.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
          cmd.ClearRenderTarget(true, false, new Color(0, 0, 0));
          context.ExecuteCommandBuffer(cmd);
          DrawLightPoint(cmd, context, camera);
          if(_frameIndex == 5)  savePng = true;
          break;
        case Status.None:
          break;
      }
    }
    finally
    {
      context.Submit();
      if (savePng) {
        Texture2D tex = new Texture2D(camera.pixelWidth, camera.pixelHeight, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight), 0, 0);
        tex.Apply();
        string path = Application.dataPath + "/Images/" + _frameIndex + ".png";
        Debug.Log(path);
        File.WriteAllBytes(path, tex.EncodeToPNG());
      }
      CommandBufferPool.Release(cmd);
    }
  }

  private void DrawScene(CommandBuffer cmd, ScriptableRenderContext context, Camera camera) {
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
      if (vlp != null) {
        for (int i = 0; i < vlp.Count(); i++) {
          cmd.DrawRenderer(vlpRenderer[i], asset.LightMaterial);
        }
      }
      context.ExecuteCommandBuffer(cmd);
    }
  }
  private void DrawLightPoint(CommandBuffer cmd, ScriptableRenderContext context, Camera camera, int vlpIdx=-1) {
    cmd.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));

    using (new ProfilingSample(cmd, "RenderScene")) {
      context.SetupCameraProperties(camera);
      if (vlpIdx == -1) {
        // for (int i = 0; i < vlp.Count(); i++) {
        //   cmd.DrawRenderer(vlpRenderer[i], asset.LightMaterial);
        // }
        for (int i = 0; i < LoopCountInOneFrame; i++) {
          cmd.DrawRenderer(vlpRenderer[(0 * LoopCountInOneFrame + i) * 9 % vlp.Count()], asset.LightMaterial);
        }
      } else {
        cmd.DrawRenderer(vlpRenderer[vlpIdx], asset.LightMaterial);
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
    var outputTarget = RequireOutputTarget(camera);
    cmd.SetRenderTarget(new RenderTargetIdentifier(outputTarget));
    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0));
    context.ExecuteCommandBuffer(cmd);
    var outputTargetSize = RequireOutputTargetSize(camera);

    var accelerationStructure = _pipeline.RequestAccelerationStructure();
    var PRNGStates = _pipeline.RequirePRNGStates(camera);
    var lightSamplePosBuffer = ((mRayTracingRenderPipeline)_pipeline).RequireComputeBuffer(_lightSamplePosBufferId * 2, vlp.Select(x=>x.pos).ToList());
    var lightSampleIntensityBuffer = ((mRayTracingRenderPipeline)_pipeline).RequireComputeBuffer(_lightSampleIntensityBufferId, vlp.Select(x=>x.intensity).ToList());
    using (new ProfilingSample(cmd, "RayTracing"))
    {
      cmd.SetRayTracingShaderPass(_shader, "RayTracing");
      cmd.SetRayTracingAccelerationStructure(_shader, _pipeline.accelerationStructureShaderId, accelerationStructure);
      cmd.SetRayTracingIntParam(_shader, _loopCountInOneFrameId, vlpIdx >= 0 ? 1 : LoopCountInOneFrame);
      // cmd.SetRayTracingIntParam(_shader, _frameIndexShaderId, vlpIdx >= 0 ? vlpIdx : _frameIndex);
      cmd.SetRayTracingIntParam(_shader, _frameIndexShaderId, 0);
      cmd.SetRayTracingIntParam(_shader, _samplingCountOneSideId, SamplingCountOneSide);
      cmd.SetRayTracingBufferParam(_shader, _PRNGStatesShaderId, PRNGStates);
      cmd.SetRayTracingTextureParam(_shader, _outputTargetShaderId, outputTarget);
      cmd.SetRayTracingVectorParam(_shader, _outputTargetSizeShaderId, outputTargetSize);
      cmd.SetRayTracingBufferParam(_shader, _lightSampleIntensityBufferId, lightSampleIntensityBuffer);
      cmd.SetGlobalBuffer(_lightSamplePosBufferId, lightSamplePosBuffer);
      cmd.DispatchRays(_shader, "GenShadowMap", (uint) outputTarget.rt.width,
        (uint) outputTarget.rt.height, 1, camera);
    }

    context.ExecuteCommandBuffer(cmd);

    using (new ProfilingSample(cmd, "FinalBlit"))
    {
      cmd.Blit(outputTarget, BuiltinRenderTextureType.CameraTarget, Vector2.one, Vector2.zero);
      context.ExecuteCommandBuffer(cmd);
    }
  }

  
}
