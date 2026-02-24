using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class ReloadRenderPipeline
{
	[MenuItem("Tools/Reload Render Pipeline")]
	public static void OnReloadRenderPipelineSelected()
	{
		if (GraphicsSettings.defaultRenderPipeline is CustomRenderPipelineAssetBase customRenderPipelineAsset)
			customRenderPipelineAsset.ReloadRenderPipeline();
	}
}
