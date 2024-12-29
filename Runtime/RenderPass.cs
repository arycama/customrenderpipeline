using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderPass : IDisposable
    {
        protected RenderGraphBuilder renderGraphBuilder;

        // TODO: Convert to handles and remove
        private readonly List<(int, ResourceHandle<RenderTexture>, int, RenderTextureSubElement)> readTextures = new();
        private readonly List<(string, ResourceHandle<GraphicsBuffer>)> readBuffers = new();
        private readonly List<(string, ResourceHandle<GraphicsBuffer>)> writeBuffers = new();

        public List<(RenderPassDataHandle, bool)> RenderPassDataHandles { get; private set; } = new();

        protected CommandBuffer command;

        public RenderGraph RenderGraph { get; set; }
        internal string Name { get; set; }
        internal int Index { get; set; }

        public abstract void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default);

        public abstract void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer);
        public abstract void SetVector(string propertyName, Vector4 value);
        public abstract void SetVectorArray(string propertyName, Vector4[] value);
        public abstract void SetFloat(string propertyName, float value);
        public abstract void SetFloatArray(string propertyName, float[] value);
        public abstract void SetInt(string propertyName, int value);
        public abstract void SetMatrix(string propertyName, Matrix4x4 value);
        public abstract void SetMatrixArray(string propertyName, Matrix4x4[] value);
        public abstract void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value);

        protected abstract void Execute();

        protected virtual void PostExecute() { }

        public override string ToString()
        {
            return Name;
        }

        public void SetTexture(string propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            SetTexture(Shader.PropertyToID(propertyName), texture, mip, subElement);
        }

        public void ReadTexture(int propertyId, ResourceHandle<RenderTexture> texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            Assert.IsFalse(RenderGraph.IsExecuting);
            readTextures.Add((propertyId, texture, mip, subElement));
            RenderGraph.RtHandleSystem.ReadResource(texture, Index);
        }

        public void ReadTexture(string propertyName, ResourceHandle<RenderTexture> texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            ReadTexture(Shader.PropertyToID(propertyName), texture, mip, subElement);
        }

        public void ReadBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
        {
            RenderGraph.BufferHandleSystem.ReadResource(buffer, Index);
            readBuffers.Add((propertyName, buffer));
        }

        public void WriteBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
        {
            RenderGraph.BufferHandleSystem.WriteResource(buffer, Index);
            writeBuffers.Add((propertyName, buffer));
        }

        public void AddRenderPassData<T>(bool isOptional = false) where T : IRenderPassData
        {
            Assert.IsFalse(RenderGraph.IsExecuting);
            var handle = RenderGraph.ResourceMap.GetResourceHandle<T>();

            if (isOptional)
            {
                if (RenderGraph.ResourceMap.TryGetRenderPassData<T>(handle, RenderGraph.FrameIndex, out var data))
                    data.SetInputs(this);
            }
            else
            {
                var data = RenderGraph.ResourceMap.GetRenderPassData<T>(handle, RenderGraph.FrameIndex);
                data.SetInputs(this);
            }

            RenderPassDataHandles.Add((handle, isOptional));
        }

        public void Run(CommandBuffer command)
        {
            // TODO: Make configurable
            command.BeginSample(Name);
            this.command = command;

            // Move into some OnPreRender thing in buffer/RTHandles? 
            foreach (var texture in readTextures)
            {
                var handle = texture.Item2;
                SetTexture(texture.Item1, GetRenderTexture(handle), texture.Item3, texture.Item4);
            }

            readTextures.Clear();

            foreach (var buffer in readBuffers)
            {
                var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(buffer.Item2);
                if (descriptor.Target.HasFlag(GraphicsBuffer.Target.Constant))
                    SetConstantBuffer(buffer.Item1, buffer.Item2);
                else
                    SetBuffer(buffer.Item1, buffer.Item2);
            }

            readBuffers.Clear();

            foreach (var buffer in writeBuffers)
                SetBuffer(buffer.Item1, buffer.Item2);
            writeBuffers.Clear();

            SetupTargets();

            // Set any data from each pass
            foreach (var renderPassDataHandle in RenderPassDataHandles)
            {
                if (renderPassDataHandle.Item2)
                {
                    if (RenderGraph.ResourceMap.TryGetRenderPassData<IRenderPassData>(renderPassDataHandle.Item1, RenderGraph.FrameIndex, out var data))
                        data.SetProperties(this, command);
                }
                else
                {
                    var data = RenderGraph.ResourceMap.GetRenderPassData<IRenderPassData>(renderPassDataHandle.Item1, RenderGraph.FrameIndex);
                    data.SetProperties(this, command);
                }
            }

            if (renderGraphBuilder != null)
            {
                renderGraphBuilder.Execute(command, this);
                renderGraphBuilder.ClearRenderFunction();
            }

            Execute();
            PostExecute();
            command.EndSample(Name);
        }

        protected abstract void SetupTargets();

        void IDisposable.Dispose()
        {
        }

        public void SetRenderFunction(Action<CommandBuffer, RenderPass> pass)
        {
            var result = new RenderGraphBuilder();
            result.SetRenderFunction(pass);
            renderGraphBuilder = result;
        }

        public void SetRenderFunction<T>(T data, Action<CommandBuffer, RenderPass, T> pass)
        {
            var result = new RenderGraphBuilder<T>();
            result.Data = data;
            result.SetRenderFunction(pass);
            renderGraphBuilder = result;
        }

        public GraphicsBuffer GetBuffer(ResourceHandle<GraphicsBuffer> handle)
        {
            return RenderGraph.BufferHandleSystem.GetResource(handle);
        }

        public RenderTexture GetRenderTexture(ResourceHandle<RenderTexture> handle)
        {
            return RenderGraph.RtHandleSystem.GetResource(handle);
        }

        public Vector4 GetScaleLimit2D(ResourceHandle<RenderTexture> handle)
        {
            var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
            var resource = GetRenderTexture(handle);

            var scaleX = (float)descriptor.Width / resource.width;
            var scaleY = (float)descriptor.Height / resource.height;
            var limitX = MathF.Floor(resource.width * scaleX);
            var limitY = MathF.Floor(resource.height * scaleY);

            return new Vector4(scaleX, scaleY, limitX, limitY);
        }

        public Vector3 GetScale3D(ResourceHandle<RenderTexture> handle)
        {
            var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
            var resource = GetRenderTexture(handle);

            var scaleX = (float)descriptor.Width / resource.width;
            var scaleY = (float)descriptor.Height / resource.height;
            var scaleZ = (float)descriptor.VolumeDepth / resource.volumeDepth;

            return new Vector3(scaleX, scaleY, scaleZ);
        }

        public Vector3 GetLimit3D(ResourceHandle<RenderTexture> handle)
        {
            var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
            var resource = GetRenderTexture(handle);

            var scaleX = (float)descriptor.Width / resource.width;
            var scaleY = (float)descriptor.Height / resource.height;
            var scaleZ = (float)descriptor.VolumeDepth / resource.volumeDepth;

            var limitX = MathF.Floor(resource.width * scaleX);
            var limitY = MathF.Floor(resource.height * scaleY);
            var limitZ = MathF.Floor(resource.volumeDepth * scaleZ);

            return new Vector3(limitX, limitY, limitZ);
        }
    }
}
