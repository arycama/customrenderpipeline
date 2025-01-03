﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class RaytracingRenderPass : RenderPass
    {
        private RayTracingShader shader;
        private string rayGenName, shaderPassName;
        private int width, height, depth;
        private RayTracingAccelerationStructure rtas;
        private float bias, distantBias, fieldOfView;

        protected readonly List<(ResourceHandle<RenderTexture>, int)> colorBindings = new();

        public void Initialize(RayTracingShader shader, string rayGenName, string shaderPassName, RayTracingAccelerationStructure rtas, int width = 1, int height = 1, int depth = 1, float bias = 0.01f, float distantBias = 0.01f, float fieldOfView = 0)
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
            this.fieldOfView = fieldOfView;
        }

        public void WriteTexture(ResourceHandle<RenderTexture> rtHandle, int propertyId)
        {
            RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);

            var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(rtHandle);
            descriptor = new RtHandleDescriptor(descriptor.Width, descriptor.Height, descriptor.Format, descriptor.VolumeDepth, descriptor.Dimension, descriptor.IsScreenTexture, descriptor.HasMips, descriptor.AutoGenerateMips, true);
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
                command.SetRayTracingTextureParam(shader, propertyName, texture);
            else
                command.SetGlobalTexture(propertyName, texture, subElement);
        }

        public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
        {
            // only way.. :( 
            command.SetGlobalBuffer(propertyName, GetBuffer(buffer));
            // command.SetRayTracingBufferParam(shader, propertyName, buffer);
        }

        public override void SetVector(string propertyName, Vector4 value)
        {
            command.SetRayTracingVectorParam(shader, propertyName, value);
        }

        public override void SetVectorArray(string propertyName, Vector4[] value)
        {
            command.SetRayTracingVectorArrayParam(shader, propertyName, value);
        }

        public override void SetFloat(string propertyName, float value)
        {
            command.SetRayTracingFloatParam(shader, propertyName, value);
        }

        public override void SetFloatArray(string propertyName, float[] value)
        {
            command.SetRayTracingFloatParams(shader, propertyName, value);
        }

        public override void SetInt(string propertyName, int value)
        {
            command.SetRayTracingIntParam(shader, propertyName, value);
        }

        internal static float GetPixelSpreadTangent(float fov, int width, int height)
        {
            return Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f) * 2.0f / Mathf.Min(width, height);
        }

        internal static float GetPixelSpreadAngle(float fov, int width, int height)
        {
            return Mathf.Atan(GetPixelSpreadTangent(fov, width, height));
        }

        protected override void Execute()
        {
            Assert.IsNotNull(rtas);

            command.SetGlobalFloat("_RaytracingPixelSpreadAngle", GetPixelSpreadAngle(fieldOfView, width, height));
            command.SetRayTracingFloatParams(shader, "_RaytracingPixelSpreadAngle", GetPixelSpreadAngle(fieldOfView, width, height));

            command.SetRayTracingFloatParams(shader, "_RaytracingBias", bias);
            command.SetRayTracingFloatParams(shader, "_RaytracingDistantBias", distantBias);
            command.SetRayTracingAccelerationStructure(shader, "SceneRaytracingAccelerationStructure", rtas);
            command.SetRayTracingShaderPass(shader, shaderPassName);
            command.DispatchRays(shader, rayGenName, (uint)width, (uint)height, (uint)depth);
        }

        protected override void SetupTargets()
        {
            for (var i = 0; i < colorBindings.Count; i++)
                command.SetRayTracingTextureParam(shader, colorBindings[i].Item2, GetRenderTexture(colorBindings[i].Item1));
        }

        public override void SetMatrix(string propertyName, Matrix4x4 value)
        {
            command.SetRayTracingMatrixParam(shader, propertyName, value);
        }

        public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value)
        {
            var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
            var size = descriptor.Count * descriptor.Stride; 
            command.SetRayTracingConstantBufferParam(shader, propertyName, GetBuffer(value), 0, size);
        }

        public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
        {
            command.SetRayTracingMatrixArrayParam(shader, propertyName, value);
        }

        protected sealed override void PostExecute()
        {
            foreach (var colorTarget in colorBindings)
            {
                var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(colorTarget.Item1);
                if (descriptor.AutoGenerateMips)
                    command.GenerateMips(GetRenderTexture(colorTarget.Item1));
            }

            colorBindings.Clear();
        }
    }
}