using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

public class ObjectRenderPass : GraphicsRenderPass
{
	private List<RendererList> rendererLists = new();

	public void Initialize(string tag, ScriptableRenderContext context, CullingResults cullingResults, Camera camera, RenderQueueRange renderQueueRange, SortingCriteria sortingCriteria = SortingCriteria.None, PerObjectData perObjectData = PerObjectData.None, bool excludeMotionVectors = false)
	{
		var rendererListDesc = new RendererListDesc(new ShaderTagId(tag), cullingResults, camera)
		{
			renderQueueRange = renderQueueRange,
			sortingCriteria = sortingCriteria,
			excludeObjectMotionVectors = excludeMotionVectors,
			rendererConfiguration = perObjectData
		};

		rendererLists.Clear();
		rendererLists.Add(context.CreateRendererList(rendererListDesc));
		context.PrepareRendererListsAsync(rendererLists);
	}

	public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		Command.SetGlobalTexture(propertyName, texture);
	}

	public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
	{
		Command.SetGlobalBuffer(propertyName, GetBuffer(buffer));
	}

	public override void SetVector(int propertyName, Vector4 value)
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

	protected override void ExecuteRenderPassBuilder()
	{
		if (renderGraphBuilder != null)
		{
			renderGraphBuilder.Execute(Command, this);
			renderGraphBuilder.ClearRenderFunction();
		}
	}

	protected override void Execute()
	{
		if(rendererLists[0].isValid)
			Command.DrawRendererList(rendererLists[0]);

		rendererLists.Clear();
	}

	public override void SetMatrix(string propertyName, Matrix4x4 value)
	{
		Command.SetGlobalMatrix(propertyName, value);
	}

	public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value)
	{
		var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
		var size = descriptor.Count * descriptor.Stride;
		Command.SetGlobalConstantBuffer(GetBuffer(value), propertyName, 0, size);
	}

	public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
	{
		Command.SetGlobalMatrixArray(propertyName, value);
	}
}
