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
    GenVLPRenderer,
    DrawHlslScene,
    GenScene,
    GenSingleLightScene,
    SuggestiveContour,
    None
  }
  private Status status = Status.GenVLP;
  struct VLP {
    public Vector3 pos;
    public float intensity;
    public VLP(Vector3 pos, float intensity) {
      this.pos = pos;
      this.intensity = intensity;
    }
  }
  private readonly int _PRNGStatesShaderId = Shader.PropertyToID("_PRNGStates");

  /// <summary>
  /// the frame index.
  /// </summary>
  private int _frameIndex = 0;
  public const int VPLLoopCount = 2;
  public const int LoopCountInOneFrame = 30;
  public const int SamplingCountOneSide = 300;
  public int _VPLLoopIdx = 0;
  private readonly int _frameIndexShaderId = Shader.PropertyToID("_FrameIndex");
  private readonly int _lightSamplePosBufferId = Shader.PropertyToID("_LightSamplePosBuffer");
  private readonly int _lightSampleIntensityBufferId = Shader.PropertyToID("_LightSampleIntensityBuffer");
  private readonly int _vitualLightPointsId = Shader.PropertyToID("_VitualLightPoints");
  private readonly int _samplingCountOneSideId = Shader.PropertyToID("_SamplingCountOneSide");
  private readonly int _loopCountInOneFrameId = Shader.PropertyToID("_LoopCountInOneFrame");

  public List<Vector3> LightSamplePos;

  private List<VLP> vlp = new List<VLP>();
  private List<Renderer> vlpRenderer = new List<Renderer>();
  TestAsset asset;
  RenderTexture _tmpRT1;
  RenderTexture _tmpRT2;
  RenderTexture _oriRT;
  RenderTexture _depthRT;
  RenderTexture _ssaoRT;
  private ComputeBuffer VitualLightPointsBuffer;
  /// <summary>
  /// constructor.
  /// </summary>
  /// <param name="asset">the tutorial asset.</param>
  public Test(TestAsset asset) : base(asset)
  {
    this.asset = asset;
    LightSamplePos = asset.LightSamplePos;
    vlp = LightSamplePos.Select(x => new VLP(x, asset.intensity)).ToList();
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
          _VPLLoopIdx++;
          break;
        case Status.GetVLPFromGPU:
          int vlpLen = LightSamplePos.Count * 10;
          VLP[] _vlp = new VLP[vlpLen];
          VitualLightPointsBuffer.GetData(_vlp);
          vlp.AddRange(_vlp.Where(x=>x.intensity > 0).ToList());
          status = _VPLLoopIdx < VPLLoopCount ? Status.GenVLP : Status.GenVLPRenderer;
          break;
        case Status.GenVLPRenderer:
          if (vlpRenderer.Count == 0) {
            foreach (var v in vlp) {
              var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
              sphere.transform.position = v.pos;
              sphere.transform.localScale = new Vector3(1, 1, 1) * asset.sphereScale;
              vlpRenderer.Add(sphere.GetComponent<Renderer>());
            };
          }
          status = Status.GenSingleLightScene;
          break;
        case Status.GenSingleLightScene:
          if (camera == Camera.main) return;
          if (_frameIndex < LoopCountInOneFrame) {
            GenerateScene(cmd, context, camera, _frameIndex);
            savePng = true;
            DrawLightPoint(cmd, context, camera, _frameIndex);
            _frameIndex++;
          }
          else {
            status = Status.GenScene;
            _frameIndex = 0;
          }
          break;
        case Status.DrawHlslScene:
          if (camera == Camera.main) return;
          DrawScene(cmd, context, camera);

          if (_tmpRT1 == null) {
            _tmpRT1 = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
            _tmpRT2 = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
            _oriRT = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
            _depthRT = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
            _ssaoRT = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
          }          
          if (camera.GetComponent<SSAOEffect>()) {
              cmd.Blit(BuiltinRenderTextureType.CameraTarget, _oriRT, Vector2.one, Vector2.zero);
              GetSceneDepth(cmd, context, camera);
              cmd.Blit(BuiltinRenderTextureType.CameraTarget, _depthRT, Vector2.one, Vector2.zero);
              camera.GetComponent<SSAOEffect>().DoSSAO(camera, cmd, _oriRT, _tmpRT1, _depthRT);
              cmd.Blit(_tmpRT1, BuiltinRenderTextureType.CameraTarget, Vector2.one, Vector2.zero);
              context.ExecuteCommandBuffer(cmd);
          }
          break;
        case Status.GenScene:
          if (camera.cameraType != CameraType.Game) return;
          if (camera == Camera.main) return;
          _frameIndex++;
          GenerateScene(cmd, context, camera);

          // suggestive contour
          if (_tmpRT1 == null) {
            _tmpRT1 = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
            _tmpRT2 = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
            _oriRT = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
            _depthRT = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
            _ssaoRT = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight);
          }
          SSAOEffect ssao = camera.GetComponent<SSAOEffect>();
          cmd.Blit(BuiltinRenderTextureType.CameraTarget, _oriRT);
          if (ssao && ssao.enabled) {
              GetSceneDepth(cmd, context, camera);
              cmd.Blit(BuiltinRenderTextureType.CameraTarget, _depthRT, Vector2.one, Vector2.zero);
              ssao.DoSSAO(camera, cmd, _oriRT, _ssaoRT, _depthRT);
              cmd.Blit(_ssaoRT, BuiltinRenderTextureType.CameraTarget, Vector2.one, Vector2.zero);
              context.ExecuteCommandBuffer(cmd);
          }

          if (asset.needEdge) {
            cmd.Blit(_oriRT, _tmpRT1, asset.EdgeMaterial, 0);

            asset.EdgeMaterial.SetTexture("_EdgeTex", _tmpRT1);
            cmd.Blit(_oriRT, _tmpRT2, asset.EdgeMaterial, 1);
            context.ExecuteCommandBuffer(cmd);
            
            asset.EdgeMaterial.SetTexture("_EdgeTex", _tmpRT1);
            cmd.Blit(_oriRT, BuiltinRenderTextureType.CameraTarget, asset.EdgeMaterial, 2);
            cmd.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
            cmd.ClearRenderTarget(true, false, new Color(0, 0, 0));
            context.ExecuteCommandBuffer(cmd);
          }
          
		     
          DrawLightPoint(cmd, context, camera);
          // if(_frameIndex == 5)  savePng = true;
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

  private void GetSceneDepth(CommandBuffer cmd, ScriptableRenderContext context, Camera camera) {
    cmd.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0));
    context.ExecuteCommandBuffer(cmd);
    cmd.Clear();

    using (new ProfilingSample(cmd, "RenderScene")) {
      context.SetupCameraProperties(camera);
      context.DrawSkybox(camera);
      foreach (var renderer in SceneManager.Instance.renderers) {
        cmd.DrawRenderer(renderer, asset.StandardMaterial, 0, 1);
      }
      context.ExecuteCommandBuffer(cmd);
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
      for (int i = 0; i < vlpRenderer.Count; i++) {
        cmd.DrawRenderer(vlpRenderer[i], asset.LightMaterial);
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
        cmd.DrawRenderer(vlpRenderer[(0 * LoopCountInOneFrame + vlpIdx) * 9 % vlp.Count()], asset.LightMaterial);
      }
      context.ExecuteCommandBuffer(cmd);
    }
  }

  private void GenerateVPL(CommandBuffer cmd, ScriptableRenderContext context, Camera camera) {
    int vlpLen = LightSamplePos.Count * 10;
    VitualLightPointsBuffer = new ComputeBuffer(vlpLen, sizeof(float) * 4);
    var accelerationStructure = _pipeline.RequestAccelerationStructure();
    var PRNGStates = _pipeline.RequirePRNGStates(camera);
    var lightSamplePosBuffer = ((mRayTracingRenderPipeline)_pipeline).RequireComputeBuffer(_lightSamplePosBufferId, LightSamplePos);
    using (new ProfilingSample(cmd, "RayTracing")) {
      cmd.SetRayTracingShaderPass(_shader, "RayTracing");
      cmd.SetRayTracingAccelerationStructure(
        _shader, _pipeline.accelerationStructureShaderId, accelerationStructure);
      cmd.SetRayTracingBufferParam(_shader, _PRNGStatesShaderId, PRNGStates);
      cmd.SetRayTracingIntParam(_shader, "_VPLLoopIdx", _VPLLoopIdx);
      cmd.SetRayTracingFloatParam(_shader, "_Intensity", asset.intensity);
      cmd.SetGlobalBuffer(_vitualLightPointsId, VitualLightPointsBuffer);
      cmd.SetGlobalBuffer(_lightSamplePosBufferId, lightSamplePosBuffer);
      cmd.DispatchRays(_shader, "PutLightSource", (uint)LightSamplePos.Count, 1, 1, camera);

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
      cmd.SetRayTracingIntParam(_shader, _loopCountInOneFrameId, LoopCountInOneFrame);
      cmd.SetRayTracingIntParam(_shader, _frameIndexShaderId, vlpIdx >= 0 ? vlpIdx : 0);
      cmd.SetRayTracingIntParam(_shader, _samplingCountOneSideId, SamplingCountOneSide);
      cmd.SetRayTracingBufferParam(_shader, _PRNGStatesShaderId, PRNGStates);
      cmd.SetRayTracingTextureParam(_shader, _outputTargetShaderId, outputTarget);
      cmd.SetRayTracingVectorParam(_shader, _outputTargetSizeShaderId, outputTargetSize);
      cmd.SetGlobalBuffer(_lightSampleIntensityBufferId, lightSampleIntensityBuffer);
      cmd.SetGlobalBuffer(_lightSamplePosBufferId, lightSamplePosBuffer);
      cmd.DispatchRays(_shader, vlpIdx >= 0 ? "GenSingleShadowMap" : "GenShadowMap", (uint) outputTarget.rt.width,
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
