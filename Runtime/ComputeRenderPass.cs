using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ComputeRenderPass : RenderPass
    {
        private ComputeShader computeShader;
        private int kernelIndex, xThreads, yThreads, zThreads;
        private bool normalizedDispatch;

        protected readonly List<(RTHandle, string)> colorBindings = new();

        public void Initialize(ComputeShader computeShader, int kernelIndex, int xThreads, int yThreads = 1, int zThreads = 1, bool normalizedDispatch = true)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.kernelIndex = kernelIndex;
            this.xThreads = xThreads;
            this.yThreads = yThreads;
            this.zThreads = zThreads;
            this.normalizedDispatch = normalizedDispatch;
        }

        public void WriteTexture(string propertyName, RTHandle handle)
        {
            handle.EnableRandomWrite = true;

            colorBindings.Add(new(handle, propertyName));
            RenderGraph.SetRTHandleWrite(handle, Index);
        }

        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture)
        {
            command.SetComputeTextureParam(computeShader, kernelIndex, propertyName, texture);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer)
        {
            command.SetComputeBufferParam(computeShader, kernelIndex, propertyName, buffer);
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            command.SetComputeVectorParam(computeShader, propertyName, value);
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

        protected override void Execute(CommandBuffer command)
        {
            if (normalizedDispatch)
                command.DispatchNormalized(computeShader, kernelIndex, xThreads, yThreads, zThreads);
            else
                command.DispatchCompute(computeShader, kernelIndex, xThreads, yThreads, zThreads);
        }

        protected override void SetupTargets(CommandBuffer command)
        {
            for (var i = 0; i < colorBindings.Count; i++)
                command.SetComputeTextureParam(computeShader, kernelIndex, colorBindings[i].Item2, colorBindings[i].Item1);

            colorBindings.Clear();
            screenWrite = false;
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
    }
}