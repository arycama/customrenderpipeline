using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class IndirectComputeRenderPass : RenderPass
    {
        private ComputeShader computeShader;
        private int kernelIndex;
        private uint argsOffset;
        private GraphicsBuffer indirectBuffer;

        protected readonly List<(RTHandle, string)> colorBindings = new();
        private readonly List<string> keywords = new();

        public void Initialize(ComputeShader computeShader, GraphicsBuffer indirectBuffer, int kernelIndex = 0, uint argsOffset = 0)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.kernelIndex = kernelIndex;
            this.indirectBuffer = indirectBuffer;
            this.argsOffset = argsOffset;
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

        protected override void Execute(CommandBuffer command)
        {
            foreach (var keyword in keywords)
                command.EnableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            command.DispatchCompute(computeShader, kernelIndex, indirectBuffer, argsOffset);

            foreach (var keyword in keywords)
                command.DisableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            keywords.Clear();
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

        public void AddKeyword(string keyword)
        {
            keywords.Add(keyword);
        }
    }
}