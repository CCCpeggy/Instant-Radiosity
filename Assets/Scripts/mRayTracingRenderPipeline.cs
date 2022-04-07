using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class mRayTracingRenderPipeline : RayTracingRenderPipeline
{
  private readonly Dictionary<int, ComputeBuffer> Buffers = new Dictionary<int, ComputeBuffer>();

  public mRayTracingRenderPipeline(RayTracingRenderPipelineAsset asset): base(asset)
  {
  }

  public ComputeBuffer RequireComputeBuffer(int id, List<Vector3> data)
  {
    if (Buffers.TryGetValue(id, out var buffer))
      return buffer;

    buffer = new ComputeBuffer(data.Count, sizeof(float) * 3);
    buffer.SetData(data);

    Buffers.Add(id, buffer);
    return buffer;
  }

  public ComputeBuffer RequireComputeBuffer(int id, List<float> data)
  {
    if (Buffers.TryGetValue(id, out var buffer))
      return buffer;

    buffer = new ComputeBuffer(data.Count, sizeof(float));
    buffer.SetData(data);

    Buffers.Add(id, buffer);
    return buffer;
  }

  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposed);
    foreach (var pair in Buffers)
    {
      pair.Value.Release();
    }
  }
}
