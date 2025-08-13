using UnityEngine;
using UnityEngine.Rendering;

public class ShadowRenderPass : RenderPass<ShadowRenderPass>
{
	private RendererList rendererList;
	private float bias, slopeBias;
	private bool zClip;
	private bool isPointLight;

	public void WriteTexture(ResourceHandle<RenderTexture> rtHandle)
	{
		RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);
	}

	public void Initialize(ScriptableRenderContext context, CullingResults cullingResults, int lightIndex, BatchCullingProjectionType projectionType, ShadowSplitData shadowSplitData, float bias, float slopeBias, bool zClip, bool isPointLight)
	{
		this.bias = bias;
		this.slopeBias = slopeBias;
		this.zClip = zClip;
		this.isPointLight = isPointLight;

		var shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIndex, projectionType)
		{
			splitData = shadowSplitData
		};

		rendererList = context.CreateShadowRendererList(ref shadowDrawingSettings);
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
		Command.SetGlobalDepthBias(bias, slopeBias);
		Command.SetGlobalFloat("_ZClip", zClip ? 1.0f : 0.0f);

		if (isPointLight)
			Command.EnableShaderKeyword("POINT_LIGHT");

		Command.DrawRendererList(rendererList);

		if (isPointLight)
			Command.DisableShaderKeyword("POINT_LIGHT");

		Command.SetGlobalDepthBias(0.0f, 0.0f);
		Command.SetGlobalFloat("_ZClip", 1.0f);
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

	protected override void SetupTargets()
	{
	}
}