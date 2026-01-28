using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class BlitToScreenPass<T> : RenderPass<T>
{
	private Material material;
    private RenderTargetIdentifier target;
    private int passIndex;
    private GraphicsFormat format;

    private readonly List<ResourceHandle<RenderTexture>> frameBufferInputs = new();

    public override bool IsNativeRenderPass => true;

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public void Initialize(Material material, RenderTargetIdentifier target, GraphicsFormat format, Int3 size, int passIndex = 0)
	{
		this.material = material;
		this.passIndex = passIndex;
        this.target = new RenderTargetIdentifier(target, 0, CubemapFace.Unknown, -1);
        this.format = format;
        this.size = size;
    }

	public override void Reset()
	{
		base.Reset();
		material = null;
		passIndex = 0;
        frameBufferInputs.Clear();
    }

    public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		switch (subElement)
		{
			case RenderTextureSubElement.Depth:
				PropertyBlock.SetTexture(propertyName, (RenderTexture)texture, RenderTextureSubElement.Depth);
				break;
			case RenderTextureSubElement.Stencil:
				PropertyBlock.SetTexture(propertyName, (RenderTexture)texture, RenderTextureSubElement.Stencil);
				break;
			default:
				PropertyBlock.SetTexture(propertyName, texture);
				break;
		}
	}

	public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
	{
		PropertyBlock.SetBuffer(propertyName, GetBuffer(buffer));
	}

	public override void SetVector(int propertyName, Float4 value)
	{
		PropertyBlock.SetVector(propertyName, value);
	}

	public override void SetVectorArray(string propertyName, Vector4[] value)
	{
		PropertyBlock.SetVectorArray(propertyName, value);
	}

	public override void SetFloat(string propertyName, float value)
	{
		PropertyBlock.SetFloat(propertyName, value);
	}

	public override void SetFloatArray(string propertyName, float[] value)
	{
		PropertyBlock.SetFloatArray(propertyName, value);
	}

	public override void SetInt(string propertyName, int value)
	{
		PropertyBlock.SetInt(propertyName, value);
	}

	public override void SetMatrix(string propertyName, Matrix4x4 value)
	{
		PropertyBlock.SetMatrix(propertyName, value);
	}

	public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset)
	{
		var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
		if(size == 0)
			size = descriptor.Count * descriptor.Stride;
		PropertyBlock.SetConstantBuffer(propertyName, GetBuffer(value), offset, size);
	}

	public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
	{
		PropertyBlock.SetMatrixArray(propertyName, value);
	}

    public override void SetupRenderPassData()
    {
        {
            var descriptor = new AttachmentDescriptor(format) { loadStoreTarget = target, storeAction = RenderBufferStoreAction.Store };
            outputs.Add(descriptor);
        }

        foreach (var input in frameBufferInputs)
        {
            var handleData = RenderGraph.RtHandleSystem.GetHandleData(input);

            var target = GetRenderTexture(input);
            var descriptor = new AttachmentDescriptor(handleData.descriptor.format)
            {
                loadAction = RenderBufferLoadAction.Load,
                storeAction = handleData.freeIndex1 == Index ? RenderBufferStoreAction.DontCare : RenderBufferStoreAction.Store,
                loadStoreTarget = new RenderTargetIdentifier(target, 0, CubemapFace.Unknown, -1),
            };

            inputs.Add(descriptor);
        }
    }

    protected override void Execute()
    {
        foreach (var keyword in keywords)
            Command.EnableKeyword(material, new LocalKeyword(material.shader, keyword));

        Command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3 * size.z, 1, PropertyBlock);

        foreach (var keyword in keywords)
            Command.DisableKeyword(material, new LocalKeyword(material.shader, keyword));
    }

    public void ReadFrameBuffer(ResourceHandle<RenderTexture> rtHandle)
    {
        frameBufferInputs.Add(rtHandle);
        RenderGraph.RtHandleSystem.ReadResource(rtHandle, Index);
    }
}