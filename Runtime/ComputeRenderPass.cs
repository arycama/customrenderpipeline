﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ComputeRenderPass : RenderPass
    {
        private ComputeShader computeShader;
        private int kernelIndex, xThreads, yThreads, zThreads;
        private bool normalizedDispatch;

        protected readonly List<(RTHandle, int, int)> colorBindings = new();
        private readonly List<string> keywords = new();

        public void Initialize(ComputeShader computeShader, int kernelIndex = 0, int xThreads = 1, int yThreads = 1, int zThreads = 1, bool normalizedDispatch = true)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.kernelIndex = kernelIndex;
            this.xThreads = xThreads;
            this.yThreads = yThreads;
            this.zThreads = zThreads;
            this.normalizedDispatch = normalizedDispatch;
        }

        public void WriteTexture(int propertyId, RTHandle texture, int mip = 0)
        {
            colorBindings.Add(new(texture, propertyId, mip));
            texture.EnableRandomWrite = true;

            if (!texture.IsPersistent || !texture.IsAssigned)
            {
                if (texture.IsPersistent)
                    texture.IsAssigned = true;

                RenderGraph.SetRTHandleWrite(texture, Index);
            }
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

        protected override void Execute(CommandBuffer command)
        {
            foreach(var keyword in keywords)
                command.EnableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            if (normalizedDispatch)
                command.DispatchNormalized(computeShader, kernelIndex, xThreads, yThreads, zThreads);
            else
            {
                Assert.IsTrue(xThreads > 0);
                Assert.IsTrue(yThreads > 0);
                Assert.IsTrue(zThreads > 0);

                command.DispatchCompute(computeShader, kernelIndex, xThreads, yThreads, zThreads);
            }

            foreach (var keyword in keywords)
                command.DisableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            keywords.Clear();
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