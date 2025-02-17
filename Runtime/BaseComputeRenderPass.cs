﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class BaseComputeRenderPass : RenderPass
    {
        protected ComputeShader computeShader;
        protected int kernelIndex;
        protected readonly List<(ResourceHandle<RenderTexture>, int, int)> colorBindings = new();
        protected readonly List<string> keywords = new();

        public void WriteTexture(int propertyId, ResourceHandle<RenderTexture> rtHandle, int mip = 0)
        {
            RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);
            colorBindings.Add(new(rtHandle, propertyId, mip));

            var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(rtHandle);
            descriptor = new RtHandleDescriptor(descriptor.Width, descriptor.Height, descriptor.Format, descriptor.VolumeDepth, descriptor.Dimension, descriptor.IsScreenTexture, descriptor.HasMips, descriptor.AutoGenerateMips, true);
            RenderGraph.RtHandleSystem.SetDescriptor(rtHandle, descriptor);
        }

        public void WriteTexture(string propertyName, ResourceHandle<RenderTexture> texture, int mip = 0)
        {
            WriteTexture(Shader.PropertyToID(propertyName), texture, mip);
        }

        public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            Command.SetComputeTextureParam(computeShader, kernelIndex, propertyName, texture, mip, subElement);
        }

        public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
        {
            Command.SetComputeBufferParam(computeShader, kernelIndex, propertyName, GetBuffer(buffer));
        }

        public override void SetVector(int propertyName, Vector4 value)
        {
            Command.SetComputeVectorParam(computeShader, propertyName, value);
        }

        public override void SetVectorArray(string propertyName, Vector4[] value)
        {
            Command.SetComputeVectorArrayParam(computeShader, propertyName, value);
        }

        public override void SetFloat(string propertyName, float value)
        {
            Command.SetComputeFloatParam(computeShader, propertyName, value);
        }

        public override void SetFloatArray(string propertyName, float[] value)
        {
            Command.SetComputeFloatParams(computeShader, propertyName, value);
        }

        public override void SetInt(string propertyName, int value)
        {
            Command.SetComputeIntParam(computeShader, propertyName, value);
        }

        protected override void SetupTargets()
        {
            for (var i = 0; i < colorBindings.Count; i++)
                Command.SetComputeTextureParam(computeShader, kernelIndex, colorBindings[i].Item2, GetRenderTexture(colorBindings[i].Item1), colorBindings[i].Item3);
        }

        public override void SetMatrix(string propertyName, Matrix4x4 value)
        {
            Command.SetComputeMatrixParam(computeShader, propertyName, value);
        }

        public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value)
        {
            var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
            var size = descriptor.Count * descriptor.Stride;
            Command.SetComputeConstantBufferParam(computeShader, propertyName, GetBuffer(value), 0, size);
        }

        public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
        {
            Command.SetComputeMatrixArrayParam(computeShader, propertyName, value);
        }

        public void AddKeyword(string keyword)
        {
            keywords.Add(keyword);
        }

        protected sealed override void PostExecute()
        {
            foreach (var colorTarget in colorBindings)
            {
                var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(colorTarget.Item1);
                if (descriptor.AutoGenerateMips && descriptor.HasMips)
                    Command.GenerateMips(GetRenderTexture(colorTarget.Item1));
            }

            colorBindings.Clear();
        }
    }
}