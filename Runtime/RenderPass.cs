using System;
using System.Collections.Generic;
using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderPass : IDisposable
    {
        protected RenderGraphBuilder renderGraphBuilder;

        protected bool screenWrite;

        // TODO: Convert to handles and remove
        private readonly List<(string, RTHandle, RenderTextureSubElement)> readTextures = new();
        private readonly List<(string, BufferHandle)> readBuffers = new();
        private readonly List<(string, BufferHandle)> writeBuffers = new();

        public List<RenderPassDataHandle> RenderPassDataHandles { get; private set; } = new(); 

        public RenderGraph RenderGraph { get; set; }
        internal string Name { get; set; }
        internal int Index { get; set; }

        public abstract void SetTexture(CommandBuffer command, string propertyName, Texture texture, RenderTextureSubElement subElement = RenderTextureSubElement.Default);
        public abstract void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer);
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

        public void ReadTexture(string propertyName, RTHandle texture, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            Assert.IsFalse(RenderGraph.IsExecuting);
            Assert.IsNotNull(texture, propertyName);
            readTextures.Add((propertyName, texture, subElement));
            RenderGraph.SetLastRTHandleRead(texture, Index);
        }

        public void WriteScreen()
        {
            screenWrite = true;
        }

        public void ReadBuffer(string propertyName, BufferHandle buffer)
        {
            readBuffers.Add((propertyName, buffer));
        }

        public void WriteBuffer(string propertyName, BufferHandle buffer)
        {
            writeBuffers.Add((propertyName, buffer));
        }

        public void AddRenderPassData(RenderPassDataHandle handle)
        {
            RenderPassDataHandles.Add(handle);
        }

        public void AddRenderPassData<T>() where T : IRenderPassData
        {
            Assert.IsFalse(RenderGraph.IsExecuting);
            var handle = RenderGraph.ResourceMap.GetResourceHandle<T>();
            AddRenderPassData(handle);
        }

        public void Run(CommandBuffer command, ScriptableRenderContext context)
        {
            command.BeginSample(Name);

            // Set render pass data
            //foreach(var renderPassDataHandle in renderPassDataHandles)
            //{
            //    var data = RenderGraph.ResourceMap.GetRenderPassData<IRenderPassData>(renderPassDataHandle);
            //    data.SetInputs(this);
            //}

            // Move into some OnPreRender thing in buffer/RTHandles? 
            foreach (var texture in readTextures)
            {
                var handle = texture.Item2;
                SetTexture(command, texture.Item1, handle, texture.Item3);
                // TODO: Remove/replace with some utility function as this allocates
                SetVector(command, $"{texture.Item1}_Scale", new Vector3(handle.Scale.x, handle.Scale.y, handle.Scale.z)); 
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

            if (renderGraphBuilder != null)
            {
                // Set any data from each pass
                foreach (var renderPassDataHandle in RenderPassDataHandles)
                {
                    var data = RenderGraph.ResourceMap.GetRenderPassData<IRenderPassData>(renderPassDataHandle);
                    data.SetProperties(this, command);
                }

                renderGraphBuilder.Execute(command, context, this);
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

        public T SetRenderFunction<T>(Action<CommandBuffer, ScriptableRenderContext, RenderPass, T> pass) where T : class, new()
        {
            var result = RenderGraph.GetRenderGraphBuilder<T>();
            result.SetRenderFunction(pass);
            renderGraphBuilder = result;
            return result.Data;
        }
    }
}

public class RenderGraphBuilder
{
    private Action<CommandBuffer, ScriptableRenderContext> pass;

    public void SetRenderFunction(Action<CommandBuffer, ScriptableRenderContext> pass)
    {
        this.pass = pass;
    }

    public virtual void ClearRenderFunction()
    {
        pass = null;
    }

    public virtual void Execute(CommandBuffer command, ScriptableRenderContext context, RenderPass pass)
    {
        this.pass?.Invoke(command, context);
    }
}

public class RenderGraphBuilder<T> : RenderGraphBuilder where T : class, new()
{
    public T Data { get; } = new();
    private Action<CommandBuffer, ScriptableRenderContext, RenderPass, T> pass;

    public void SetRenderFunction(Action<CommandBuffer, ScriptableRenderContext, RenderPass, T> pass)
    {
        this.pass = pass;
    }

    public override void ClearRenderFunction()
    {
        pass = null;
    }

    public override void Execute(CommandBuffer command, ScriptableRenderContext context, RenderPass pass)
    {
        this.pass?.Invoke(command, context, pass, Data);
    }
}
