using System.Collections.Generic;
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

        protected readonly List<(RTHandle, int)> colorBindings = new();

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

        public void WriteTexture(RTHandle rtHandle, int propertyId)
        {
            SetTextureWrite(rtHandle);
            rtHandle.EnableRandomWrite = true;
            colorBindings.Add((rtHandle, propertyId));
        }

        public void WriteTexture(RTHandle texture, string propertyName)
        {
            WriteTexture(texture, Shader.PropertyToID(propertyName));
        }

        public override void SetTexture(CommandBuffer command, int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            if(subElement == RenderTextureSubElement.Depth || subElement == RenderTextureSubElement.Default)
                command.SetRayTracingTextureParam(shader, propertyName, texture);
            else
                command.SetGlobalTexture(propertyName, texture, subElement);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer)
        {
            // only way.. :( 
            command.SetGlobalBuffer(propertyName, buffer);
           // command.SetRayTracingBufferParam(shader, propertyName, buffer);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer)
        {
            // only way.. :( 
            command.SetGlobalBuffer(propertyName, buffer);
            //command.SetRayTracingBufferParam.SetBuffer(propertyName, buffer);
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

        static internal float GetPixelSpreadTangent(float fov, int width, int height)
        {
            return Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f) * 2.0f / Mathf.Min(width, height);
        }

        static internal float GetPixelSpreadAngle(float fov, int width, int height)
        {
            return Mathf.Atan(GetPixelSpreadTangent(fov, width, height));
        }

        protected override void Execute(CommandBuffer command)
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