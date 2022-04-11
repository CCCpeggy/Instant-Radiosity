using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// the output color tutorial asset.
/// </summary>
[CreateAssetMenu(fileName = "TestAsset", menuName = "Rendering/TestAsset")]
public class TestAsset : RayTracingTutorialAsset
{
  /// <summary>
  /// create tutorial.
  /// </summary>
  /// <returns>the tutorial.</returns>
  public List<Vector3> LightSamplePos = new List<Vector3>();
  public int intensity = 200;
  public int sphereScale = 1;
  public Material LightMaterial;
  public Material EdgeMaterial;
  public override RayTracingTutorial CreateTutorial()
  {
    LightSamplePos.Clear();
    // if (Sampling.position != null) {
    //   foreach (Vector3 pos in Sampling.position)
    //     LightSamplePos.Add(pos);
    // }

    // // for corneil box
    // if (LightSamplePos.Count == 0) {
    //   for (float x=-0.5f; x<=0.5f; x+=0.15f) {
    //     for (float z=-0.5f; z<=0.5f; z+=0.15f) {
    //       LightSamplePos.Add(new Vector3(x, 1.845f, z));
    //     }
    //   }
    // }
    // for church
    // LightSamplePos.Add(new Vector3(12.5f, 1.8f, 2.5f));
    // return new Test(this);
    for (float x=-5f; x<=20f; x+=25/3) {
      for (float z=-2.5f; z<=2.5f; z+=5/3) {
        LightSamplePos.Add(new Vector3(x, 11f, z));
      }
    }
    return new Test(this);
  }
}
