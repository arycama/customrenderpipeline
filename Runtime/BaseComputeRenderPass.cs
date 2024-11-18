using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class BaseComputeRenderPass : RenderPass
    {
        protected ComputeShader computeShader;
        protected int kernelIndex;
        protected readonly List<(RTHandle, int, int)> colorBindings = new();
        protected readonly List<string> keywords = new();

        public void WriteTexture(int propertyId, RTHandle rtHandle, int mip = 0)
        {
            SetTextureWrite(rtHandle);
            colorBindings.Add(new(rtHandle, propertyId, mip));
            rtHandle.EnableRandomWrite = true;
        }

        public void WriteTexture(string propertyName, RTHandle texture, int mip = 0)
        {
            WriteTexture(Shader.PropertyToID(propertyName), texture, mip);
        }

        public override void SetTexture(CommandBuffer command, int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            command.SetComputeTextureParam(computeShader, kernelIndex, propertyName, texture, mip, subElement);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer)
        {
            command.SetComputeBufferParam(computeShader, kernelIndex, propertyName, buffer);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer)
        {
            command.SetComputeBufferParam(computeShader, kernelIndex, propertyName, buffer);
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            command.SetComputeVectorParam(computeShader, propertyName, value);
        }

        public override void SetVectorArray(CommandBuffer command, string propertyName, Vector4[] value)
        {
            command.SetComputeVectorArrayParam(computeShader, propertyName, value);
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            command.SetComputeFloatParam(computeShader, propertyName, value);
        }

        public override void SetFloatArray(CommandBuffer command, string propertyName, float[] value)
        {
            command.SetComputeFloatParams(computeShader, propertyName, value);
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            command.SetComputeIntParam(computeShader, propertyName, value);
        }

        protected override void SetupTargets(CommandBuffer command)
        {
            for (var i = 0; i < colorBindings.Count; i++)
                command.SetComputeTextureParam(computeShader, kernelIndex, colorBindings[i].Item2, colorBindings[i].Item1, colorBindings[i].Item3);
        }

        public override void SetMatrix(CommandBuffer command, string propertyName, Matrix4x4 value)
        {
            command.SetComputeMatrixParam(computeShader, propertyName, value);
        }

        public override void SetConstantBuffer(CommandBuffer command, string propertyName, BufferHandle value)
        {
            command.SetComputeConstantBufferParam(computeShader, propertyName, value, 0, value.Size);
        }

        public override void SetMatrixArray(CommandBuffer command, string propertyName, Matrix4x4[] value)
        {
            command.SetComputeMatrixArrayParam(computeShader, propertyName, value);
        }

        public void AddKeyword(string keyword)
        {
            keywords.Add(keyword);
        }

        protected sealed override void PostExecute(CommandBuffer command)
        {
            foreach (var colorTarget in colorBindings)
            {
                if (colorTarget.Item1.AutoGenerateMips)
                    command.GenerateMips(colorTarget.Item1);
            }

            colorBindings.Clear();
        }
    }
}