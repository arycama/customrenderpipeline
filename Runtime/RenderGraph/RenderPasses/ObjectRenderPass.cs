using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

public class ObjectRenderPass<T> : GraphicsRenderPass<T>
{
	//private List<RendererList> rendererLists = new();
	private RendererList rendererList;

	public void Initialize(string tag, ScriptableRenderContext context, CullingResults cullingResults, Camera camera, RenderQueueRange renderQueueRange, SortingCriteria sortingCriteria = SortingCriteria.None, PerObjectData perObjectData = PerObjectData.None, bool excludeMotionVectors = false)
	{
		var rendererListDesc = new RendererListDesc(new ShaderTagId(tag), cullingResults, camera)
		{
			renderQueueRange = renderQueueRange,
			sortingCriteria = sortingCriteria,
			excludeObjectMotionVectors = excludeMotionVectors,
			rendererConfiguration = perObjectData
		};

		rendererList = context.CreateRendererList(rendererListDesc);

		//rendererLists.Clear();
		//rendererLists.Add(context.CreateRendererList(rendererListDesc));
		//context.PrepareRendererListsAsync(rendererLists);
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
			Command.EnableKeyword(new GlobalKeyword(keyword));

		Command.DrawRendererList(rendererList);

		foreach (var keyword in keywords)
			Command.DisableKeyword(new GlobalKeyword(keyword));
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
