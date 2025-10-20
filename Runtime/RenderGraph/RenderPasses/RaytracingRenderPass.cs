using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// TODO: Maybe share some cfuncionality with graphics render pass  or compute render pass
public class RaytracingRenderPass : RenderPass<RaytracingRenderPass>
{
	private RayTracingShader shader;
	private string rayGenName, shaderPassName;
	private int width, height, depth;
	private RayTracingAccelerationStructure rtas;
	private float bias, distantBias, tanHalfFov;

	protected readonly List<(ResourceHandle<RenderTexture>, int)> colorBindings = new();

	public void Initialize(RayTracingShader shader, string rayGenName, string shaderPassName, RayTracingAccelerationStructure rtas, int width = 1, int height = 1, int depth = 1, float bias = 0.01f, float distantBias = 0.01f, float tanHalfFov = 0)
	{
		this.shader = shader;
		this.rayGenName = rayGenName;
		this.shaderPassName = shaderPassName;
		this.width = width;
		this.height = height;
		this.depth = depth;
		this.rtas = rtas;
		this.bias = bias;
		this.distantBias = distantBias;
		this.tanHalfFov = tanHalfFov;
	}

	public void WriteTexture(ResourceHandle<RenderTexture> rtHandle, int propertyId)
	{
		RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);

		var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(rtHandle);
		descriptor = new RtHandleDescriptor(descriptor.width, descriptor.height, descriptor.format, descriptor.volumeDepth, descriptor.dimension, descriptor.isScreenTexture, descriptor.hasMips, descriptor.autoGenerateMips, true, descriptor.isExactSize, descriptor.clearFlags, descriptor.clearColor, descriptor.clearDepth, descriptor.clearStencil);
		colorBindings.Add((rtHandle, propertyId));
		RenderGraph.RtHandleSystem.SetDescriptor(rtHandle, descriptor);
	}

	public void WriteTexture(ResourceHandle<RenderTexture> texture, string propertyName)
	{
		WriteTexture(texture, Shader.PropertyToID(propertyName));
	}

	public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		if (subElement == RenderTextureSubElement.Depth || subElement == RenderTextureSubElement.Default)
			Command.SetRayTracingTextureParam(shader, propertyName, texture);
		else
			Command.SetGlobalTexture(propertyName, texture, subElement);
	}

	public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
	{
		var graphicsBuffer = GetBuffer(buffer);
		if(graphicsBuffer.target == GraphicsBuffer.Target.Constant)
			Command.SetRayTracingConstantBufferParam(shader, propertyName, graphicsBuffer, 0, graphicsBuffer.stride * graphicsBuffer.count);
		else
			Command.SetRayTracingBufferParam(shader, propertyName, graphicsBuffer);
	}

	public override void SetVector(int propertyName, Vector4 value)
	{
		Command.SetRayTracingVectorParam(shader, propertyName, value);
	}

	public override void SetVectorArray(string propertyName, Vector4[] value)
	{
		Command.SetRayTracingVectorArrayParam(shader, propertyName, value);
	}

	public override void SetFloat(string propertyName, float value)
	{
		Command.SetRayTracingFloatParam(shader, propertyName, value);
	}

	public override void SetFloatArray(string propertyName, float[] value)
	{
		Command.SetRayTracingFloatParams(shader, propertyName, value);
	}

	public override void SetInt(string propertyName, int value)
	{
		Command.SetRayTracingIntParam(shader, propertyName, value);
	}

	internal static float GetPixelSpreadTangent(float tanHalfFov, int width, int height)
	{
		return tanHalfFov * 2.0f / Mathf.Min(width, height);
	}

	internal static float GetPixelSpreadAngle(float tanHalfFov, int width, int height)
	{
		return Mathf.Atan(GetPixelSpreadTangent(tanHalfFov, width, height));
	}

	protected override void Execute()
	{
		Command.SetGlobalFloat("_RaytracingPixelSpreadAngle", GetPixelSpreadAngle(tanHalfFov, width, height));
		Command.SetRayTracingFloatParams(shader, "_RaytracingPixelSpreadAngle", GetPixelSpreadAngle(tanHalfFov, width, height));

		Command.SetRayTracingFloatParams(shader, "_RaytracingBias", bias);
		Command.SetRayTracingFloatParams(shader, "_RaytracingDistantBias", distantBias);
		Command.SetRayTracingShaderPass(shader, shaderPassName);
		Command.SetRayTracingAccelerationStructure(shader, "SceneRaytracingAccelerationStructure", rtas);

		Command.DispatchRays(shader, rayGenName, (uint)width, (uint)height, (uint)depth);
	}

	protected override void SetupTargets()
	{
		for (var i = 0; i < colorBindings.Count; i++)
			Command.SetRayTracingTextureParam(shader, colorBindings[i].Item2, GetRenderTexture(colorBindings[i].Item1));
	}

	public override void SetMatrix(string propertyName, Matrix4x4 value)
	{
		Command.SetRayTracingMatrixParam(shader, propertyName, value);
	}

	public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset)
	{
		var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
		if (size == 0)
			size = descriptor.Count * descriptor.Stride;
		Command.SetRayTracingConstantBufferParam(shader, propertyName, GetBuffer(value), offset, size);
	}

	public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
	{
		Command.SetRayTracingMatrixArrayParam(shader, propertyName, value);
	}

	protected sealed override void PostExecute()
	{
		foreach (var colorTarget in colorBindings)
		{
			var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(colorTarget.Item1);
			if (descriptor.autoGenerateMips && descriptor.hasMips)
				Command.GenerateMips(GetRenderTexture(colorTarget.Item1));
		}

		colorBindings.Clear();
	}

	protected override void ExecuteRenderPassBuilder()
	{
		if (renderGraphBuilder != null)
		{
			renderGraphBuilder.Execute(Command, this);
			renderGraphBuilder.ClearRenderFunction();
		}
	}
}