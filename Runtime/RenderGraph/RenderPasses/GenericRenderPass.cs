using UnityEngine;
using UnityEngine.Rendering;

/// <summary> Has no specific functionality but can be used as a general wrapper around render functionality </summary>
public class GenericRenderPass<T> : RenderPass<T>
{
    public void WriteTexture(ResourceHandle<RenderTexture> rtHandle)
    {
        RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);
    }

    public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
    {
        // Should also clean up on post render.. but 
        Command.SetGlobalTexture(propertyName, texture);
    }

    public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
    {
        if (!string.IsNullOrEmpty(propertyName))
            Command.SetGlobalBuffer(propertyName, GetBuffer(buffer));
    }

    public override void SetVector(int propertyName, Float4 value)
    {
        Command.SetGlobalVector(propertyName, value);
    }
    public override void SetVectorArray(string propertyName, Vector4[] value)
    {
        if (!string.IsNullOrEmpty(propertyName))
            Command.SetGlobalVectorArray(propertyName, value);
    }

    public override void SetFloat(string propertyName, float value)
    {
        if (!string.IsNullOrEmpty(propertyName))
            Command.SetGlobalFloat(propertyName, value);
    }

    public override void SetFloatArray(string propertyName, float[] value)
    {
        if (!string.IsNullOrEmpty(propertyName))
            Command.SetGlobalFloatArray(propertyName, value);
    }

    public override void SetInt(string propertyName, int value)
    {
        if (!string.IsNullOrEmpty(propertyName))
            Command.SetGlobalInt(propertyName, value);
    }

    protected override void Execute()
    {
        // Does nothing (Eventually could do a command.setglobalbuffer or something?)
    }

    public override void SetMatrix(string propertyName, Matrix4x4 value)
    {
        if (!string.IsNullOrEmpty(propertyName))
            Command.SetGlobalMatrix(propertyName, value);
    }

    public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset)
    {
        if (string.IsNullOrEmpty(propertyName))
            return;

        var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
        if (size == 0)
            size = descriptor.Count * descriptor.Stride;
        Command.SetGlobalConstantBuffer(GetBuffer(value), propertyName, offset, size);
    }

    public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
    {
        if (!string.IsNullOrEmpty(propertyName))
            Command.SetGlobalMatrixArray(propertyName, value);
    }
}