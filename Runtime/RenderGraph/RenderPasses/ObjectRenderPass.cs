using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unmath;
using Quaternion = Unmath.Quaternion;

public class ObjectRenderPass<T> : GraphicsRenderPass<T>
{
	private readonly List<RendererList> rendererLists = new();
	private SinglePassStereoMode stereoMode;
    private ScriptableRenderContext context;

	public void Initialize(string tag, ScriptableRenderContext context, in CullingResults cullingResults, RenderQueueRange renderQueueRange, Int2 size, Float3 viewPosition, Quaternion viewRotation, Float3 sortAxis, DistanceMetric distanceMetric, SortingCriteria sortingCriteria = SortingCriteria.None, PerObjectData perObjectData = PerObjectData.None, bool excludeMotionVectors = false, int viewCount = 1, int antiAliasing = 1, bool isScreenPass = false, SinglePassStereoMode stereoMode = SinglePassStereoMode.None)
	{
        this.context = context;
		AntiAliasing = antiAliasing;
        this.stereoMode = stereoMode;

        var sortingSettings = new SortingSettings 
        {
            worldToCameraMatrix = Float4x4.WorldToLocal(viewPosition, viewRotation),
            cameraPosition = viewPosition,
            customAxis = sortAxis,
            criteria = sortingCriteria,
            distanceMetric = distanceMetric
        };

        var drawSettings = new DrawingSettings(new ShaderTagId(tag), sortingSettings)
        {
            enableInstancing = true,
            perObjectData = perObjectData,
        };

        var filteringSettings = new FilteringSettings(renderQueueRange, excludeMotionVectorObjects: excludeMotionVectors ? 1 : 0)
        {
            forceAllMotionVectorObjects = false
        };

        var rendererListParams = new RendererListParams(cullingResults, drawSettings, filteringSettings);
		var rendererList = context.CreateRendererList(ref rendererListParams);
        rendererLists.Clear();
        rendererLists.Add(rendererList);
        context.PrepareRendererListsAsync(rendererLists);

        Size = size;
        ViewCount = viewCount;
        IsScreenPass = isScreenPass;
    }

	public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		Command.SetGlobalTexture(propertyName, texture);
	}

	public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
	{
		Command.SetGlobalBuffer(propertyName, GetBuffer(buffer));
	}

	public override void SetVector(int propertyName, Float4 value)
	{
		Command.SetGlobalVector(propertyName, value);
	}

	public override void SetVectorArray(string propertyName, Vector4[] value)
	{
		Command.SetGlobalVectorArray(propertyName, value);
	}

	public override void SetFloat(string propertyName, float value)
	{
		Command.SetGlobalFloat(propertyName, value);
	}

	public override void SetFloatArray(string propertyName, float[] value)
	{
		Command.SetGlobalFloatArray(propertyName, value);
	}

	public override void SetInt(string propertyName, int value)
	{
		Command.SetGlobalInt(propertyName, value);
	}

	protected override void Execute()
	{
        foreach (var keyword in keywords)
			Command.EnableShaderKeyword(keyword);

		if (stereoMode == SinglePassStereoMode.Instancing)
		{
            Command.SetInstanceMultiplier(2u);
            Command.EnableShaderKeyword("STEREO_INSTANCING_ON");
			Command.SetSinglePassStereo(SinglePassStereoMode.Instancing);
        }
        else if(stereoMode == SinglePassStereoMode.Multiview)
		{
            Command.EnableShaderKeyword("STEREO_MULTIVIEW_ON");
			Command.SetSinglePassStereo(SinglePassStereoMode.Multiview);
        }

        Command.DrawRendererList(rendererLists[0]);

        if (stereoMode == SinglePassStereoMode.Instancing)
		{
            Command.SetSinglePassStereo(SinglePassStereoMode.None);
            Command.DisableShaderKeyword("STEREO_INSTANCING_ON");
            Command.SetInstanceMultiplier(1u);
        }
        else if(stereoMode == SinglePassStereoMode.Multiview)
		{
            Command.SetSinglePassStereo(SinglePassStereoMode.None);
            Command.DisableShaderKeyword("STEREO_MULTIVIEW_ON");
        }

        foreach (var keyword in keywords)
			Command.DisableShaderKeyword(keyword);
	}

	public override void SetMatrix(string propertyName, Matrix4x4 value)
	{
		Command.SetGlobalMatrix(propertyName, value);
	}

	public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset)
	{
		var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
		if (size == 0)
			size = descriptor.Count * descriptor.Stride;
		Command.SetGlobalConstantBuffer(GetBuffer(value), propertyName, offset, size);
	}

	public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
	{
		Command.SetGlobalMatrixArray(propertyName, value);
	}
}
