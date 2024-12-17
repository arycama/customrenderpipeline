using Arycama.CustomRenderPipeline;
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
        private readonly List<(int, RTHandle, int, RenderTextureSubElement)> readTextures = new();
        private readonly List<(string, BufferHandle)> readBuffers = new();
        private readonly List<(string, BufferHandle)> writeBuffers = new();

        public List<(RenderPassDataHandle, bool)> RenderPassDataHandles { get; private set; } = new();

        public RenderGraph RenderGraph { get; set; }
        internal string Name { get; set; }
        internal int Index { get; set; }

        public abstract void SetTexture(CommandBuffer command, int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default);

        public abstract void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer);
        public abstract void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer);
        public abstract void SetVector(CommandBuffer command, string propertyName, Vector4 value);
        public abstract void SetVectorArray(CommandBuffer command, string propertyName, Vector4[] value);
        public abstract void SetFloat(CommandBuffer command, string propertyName, float value);
        public abstract void SetFloatArray(CommandBuffer command, string propertyName, float[] value);
        public abstract void SetInt(CommandBuffer command, string propertyName, int value);
        public abstract void SetMatrix(CommandBuffer command, string propertyName, Matrix4x4 value);
        public abstract void SetMatrixArray(CommandBuffer command, string propertyName, Matrix4x4[] value);
        public abstract void SetConstantBuffer(CommandBuffer command, string propertyName, BufferHandle value);

        protected abstract void Execute(CommandBuffer command);

        protected virtual void PostExecute(CommandBuffer command) { }

        public override string ToString()
        {
            return Name;
        }

        public void SetTexture(CommandBuffer command, string propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            SetTexture(command, Shader.PropertyToID(propertyName), texture, mip, subElement);
        }

        public void ReadTexture(int propertyId, RTHandle texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            Assert.IsFalse(RenderGraph.IsExecuting);
            Assert.IsNotNull(texture);
            readTextures.Add((propertyId, texture, mip, subElement));
            RenderGraph.SetLastRTHandleRead(texture, Index);
        }

        public void ReadTexture(string propertyName, RTHandle texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            ReadTexture(Shader.PropertyToID(propertyName), texture, mip, subElement);
        }

        protected void SetTextureWrite(RTHandle texture)
        {
            if (!texture.IsPersistent || !texture.IsAssigned)
            {
                if (texture.IsPersistent)
                    texture.IsAssigned = true;

                RenderGraph.SetRTHandleWrite(texture, Index);
            }
        }

        public void ReadBuffer(string propertyName, BufferHandle buffer)
        {
            readBuffers.Add((propertyName, buffer));
        }

        public void WriteBuffer(string propertyName, BufferHandle buffer)
        {
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
            command.BeginSample(Name);

            // Set render pass data
            //foreach(var renderPassDataHandle in renderPassDataHandles)
            //{
            //    var data = RenderGraph.GetResource<IRenderPassData>(renderPassDataHandle);
            //    data.SetInputs(this);
            //}

            // Move into some OnPreRender thing in buffer/RTHandles? 
            foreach (var texture in readTextures)
            {
                var handle = texture.Item2;
                SetTexture(command, texture.Item1, handle, texture.Item3, texture.Item4);
            }

            readTextures.Clear();

            foreach (var buffer in readBuffers)
            {
                if (buffer.Item2.Target.HasFlag(GraphicsBuffer.Target.Constant))
                    SetConstantBuffer(command, buffer.Item1, buffer.Item2);
                else
                    SetBuffer(command, buffer.Item1, buffer.Item2);
            }
            readBuffers.Clear();

            foreach (var buffer in writeBuffers)
                SetBuffer(command, buffer.Item1, buffer.Item2);
            writeBuffers.Clear();

            SetupTargets(command);

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

            Execute(command);

            PostExecute(command);

            if (renderGraphBuilder != null)
            {
                RenderGraph.ReleaseRenderGraphBuilder(renderGraphBuilder);
                renderGraphBuilder = null;
            }

            RenderPassDataHandles.Clear();

            command.EndSample(Name);
        }

        protected abstract void SetupTargets(CommandBuffer command);

        void IDisposable.Dispose()
        {
            RenderGraph.AddRenderPassInternal(this);
        }

        public void SetRenderFunction(Action<CommandBuffer, RenderPass> pass)
        {
            var result = RenderGraph.GetRenderGraphBuilder();
            result.SetRenderFunction(pass);
            renderGraphBuilder = result;
        }

        public void SetRenderFunction<T>(T data, Action<CommandBuffer, RenderPass, T> pass)
        {
            var result = RenderGraph.GetRenderGraphBuilder<T>();
            result.Data = data;
            result.SetRenderFunction(pass);
            renderGraphBuilder = result;
        }
    }
}

//public interface IRenderGraphBuilder
//{
//    void ClearRenderFunction();
//    void Execute(CommandBuffer command, RenderPass pass);
//}

public class RenderGraphBuilder //: IRenderGraphBuilder
{
    private Action<CommandBuffer, RenderPass> pass;

    public void SetRenderFunction(Action<CommandBuffer, RenderPass> pass)
    {
        this.pass = pass;
    }

    public virtual void ClearRenderFunction()
    {
        pass = null;
    }

    public virtual void Execute(CommandBuffer command, RenderPass pass)
    {
        this.pass?.Invoke(command, pass);
    }
}

public class RenderGraphBuilder<T> : RenderGraphBuilder
{
    public T Data { get; set; }
    private Action<CommandBuffer, RenderPass, T> pass;

    public void SetRenderFunction(Action<CommandBuffer, RenderPass, T> pass)
    {
        this.pass = pass;
    }

    public override void ClearRenderFunction()
    {
        pass = null;
    }

    public override void Execute(CommandBuffer command, RenderPass pass)
    {
        this.pass?.Invoke(command, pass, Data);
    }
}
