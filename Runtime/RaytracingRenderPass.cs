using System.Collections.Generic;
using UnityEngine;
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

        protected readonly List<(RTHandle, int)> colorBindings = new();

        public void Initialize(RayTracingShader shader, string rayGenName, string shaderPassName, RayTracingAccelerationStructure rtas, int width = 1, int height = 1, int depth = 1)
        {
            this.shader = shader;
            this.rayGenName = rayGenName;
            this.shaderPassName = shaderPassName;
            this.width = width;
            this.height = height;
            this.depth = depth;
            this.rtas = rtas;
        }

        public void WriteTexture(RTHandle handle, int propertyId)
        {
            handle.EnableRandomWrite = true;
            colorBindings.Add(new(handle, propertyId));
            RenderGraph.SetRTHandleWrite(handle, Index);
        }

        public void WriteTexture(RTHandle handle, string propertyName)
        {
            WriteTexture(handle, Shader.PropertyToID(propertyName));
        }

        public override void SetTexture(CommandBuffer command, int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            command.SetRayTracingTextureParam(shader, propertyName, texture);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer)
        {
            //command.SetRayTracingBufferParam(shader, propertyName, buffer);
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            command.SetRayTracingVectorParam(shader, propertyName, value);
        }

        public override void SetVectorArray(CommandBuffer command, string propertyName, Vector4[] value)
        {
            command.SetRayTracingVectorArrayParam(shader, propertyName, value);
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            command.SetRayTracingFloatParam(shader, propertyName, value);
        }

        public override void SetFloatArray(CommandBuffer command, string propertyName, float[] value)
        {
            command.SetRayTracingFloatParams(shader, propertyName, value);
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            command.SetRayTracingIntParam(shader, propertyName, value);
        }

        protected override void Execute(CommandBuffer command)
        {
            command.SetRayTracingAccelerationStructure(shader, "SceneRaytracingAccelerationStructure", rtas);
            command.SetRayTracingShaderPass(shader, shaderPassName);
            command.DispatchRays(shader, rayGenName, (uint)width, (uint)height, (uint)depth);
        }

        protected override void SetupTargets(CommandBuffer command)
        {
            for (var i = 0; i < colorBindings.Count; i++)
                command.SetRayTracingTextureParam(shader, colorBindings[i].Item2, colorBindings[i].Item1);
        }

        public override void SetMatrix(CommandBuffer command, string propertyName, Matrix4x4 value)
        {
            command.SetRayTracingMatrixParam(shader, propertyName, value);
        }

        public override void SetConstantBuffer(CommandBuffer command, string propertyName, BufferHandle value)
        {
            command.SetRayTracingConstantBufferParam(shader, propertyName, value, 0, value.Size);
        }

        public override void SetMatrixArray(CommandBuffer command, string propertyName, Matrix4x4[] value)
        {
            command.SetRayTracingMatrixArrayParam(shader, propertyName, value);
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