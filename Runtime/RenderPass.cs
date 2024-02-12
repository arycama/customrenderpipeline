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
        private readonly List<(string, RTHandle)> readTextures = new();
        private readonly List<(string, BufferHandle)> readBuffers = new();
        private readonly List<(string, BufferHandle)> writeBuffers = new();

        public RenderGraph RenderGraph { get; set; }
        internal string Name { get; set; }
        internal int Index { get; set; }

        public abstract void SetTexture(CommandBuffer command, string propertyName, Texture texture);
        public abstract void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer);
        public abstract void SetVector(CommandBuffer command, string propertyName, Vector4 value);
        public abstract void SetFloat(CommandBuffer command, string propertyName, float value);
        public abstract void SetInt(CommandBuffer command, string propertyName, int value);
        public abstract void SetMatrix(CommandBuffer command, string propertyName, Matrix4x4 value);
        public abstract void SetMatrixArray(CommandBuffer command, string propertyName, Matrix4x4[] value);
        public abstract void SetConstantBuffer(CommandBuffer command, string propertyName, BufferHandle value);

        protected abstract void Execute(CommandBuffer command);

        public void ReadTexture(string propertyName, RTHandle texture)
        {
            Assert.IsNotNull(texture, propertyName);
            readTextures.Add((propertyName, texture));
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

        public void Run(CommandBuffer command, ScriptableRenderContext context)
        {
            command.BeginSample(Name);

            foreach (var texture in readTextures)
            {
                var handle = texture.Item2;
                SetTexture(command, texture.Item1, handle);
                SetVector(command, $"{texture.Item1}_Scale", new Vector4(handle.Scale.x, handle.Scale.y, 1.0f / handle.Scale.x, 1.0f / handle.Scale.y));
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
                renderGraphBuilder.Execute(command, context, this);
                renderGraphBuilder.ClearRenderFunction();
            }

            Execute(command);

            if (renderGraphBuilder != null)
            {
                RenderGraph.ReleaseRenderGraphBuilder(renderGraphBuilder);
                renderGraphBuilder = null;
            }

            command.EndSample(Name);
        }

        protected abstract void SetupTargets(CommandBuffer command);
        //{
        //    // TODO: Can clear a depth and color target together
        //    int width = 0, height = 0, targetWidth = 0, targetHeight = 0;

        //    var binding = new RenderTargetBinding();
        //    if (depthBinding.Handle != null)
        //    {
        //        width = depthBinding.Handle.Width;
        //        height = depthBinding.Handle.Height;
        //        targetWidth = depthBinding.Handle.RenderTexture.width;
        //        targetHeight = depthBinding.Handle.RenderTexture.height;

        //        // Load action not supported outside of renderpass API, so emulate it here
        //        if (depthBinding.LoadAction == RenderBufferLoadAction.Clear)
        //        {
        //            command.SetRenderTarget(BuiltinRenderTextureType.None, depthBinding.Handle);
        //            command.ClearRenderTarget(true, false, default, depthBinding.ClearDepth);
        //            binding.depthLoadAction = RenderBufferLoadAction.Load;
        //        }
        //        else
        //        {
        //            binding.depthLoadAction = depthBinding.LoadAction;
        //        }

        //        binding.depthRenderTarget = depthBinding.Handle;
        //        binding.depthStoreAction = depthBinding.StoreAction;
        //        binding.flags = depthBinding.Flags;
        //    }
        //    else
        //    {
        //        // Need to set binding.depthRenderTarget to BuiltinRenderTextureType.None or the pass won't work
        //        binding.depthRenderTarget = BuiltinRenderTextureType.None;
        //        binding.depthLoadAction = RenderBufferLoadAction.DontCare;
        //        binding.depthStoreAction = RenderBufferStoreAction.DontCare;
        //    }

        //    if (colorBindings.Count > 0)
        //    {
        //        var targets = ArrayPool<RenderTargetIdentifier>.Get(colorBindings.Count);
        //        var loadActions = ArrayPool<RenderBufferLoadAction>.Get(colorBindings.Count);
        //        var storeActions = ArrayPool<RenderBufferStoreAction>.Get(colorBindings.Count);

        //        for (var i = 0; i < colorBindings.Count; i++)
        //        {
        //            var target = colorBindings[i];

        //            Assert.IsTrue(targetWidth == 0 || targetWidth == target.Handle.RenderTexture.width, Name);
        //            Assert.IsTrue(targetHeight == 0 || targetHeight == target.Handle.RenderTexture.height, Name);

        //            width = target.Handle.Width;
        //            height = target.Handle.Height;

        //            // Load action not supported outside of renderpass API, so emulate it here
        //            if (target.LoadAction == RenderBufferLoadAction.Clear)
        //            {
        //                command.SetRenderTarget(target.Handle);
        //                command.ClearRenderTarget(false, true, target.ClearColor);
        //                loadActions[i] = RenderBufferLoadAction.Load;
        //            }
        //            else
        //            {
        //                loadActions[i] = target.LoadAction;
        //            }

        //            targets[i] = new RenderTargetIdentifier(target.Handle, 0, target.CubemapFace, target.DepthSlice);
        //            storeActions[i] = target.StoreAction;
        //        }

        //        binding.colorRenderTargets = targets;
        //        binding.colorLoadActions = loadActions;
        //        binding.colorStoreActions = storeActions;

        //        ArrayPool<RenderTargetIdentifier>.Release(targets);
        //        ArrayPool<RenderBufferLoadAction>.Release(loadActions);
        //        ArrayPool<RenderBufferStoreAction>.Release(storeActions);
        //    }

        //    if (screenWrite)
        //        command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        //    else if (depthBinding.Handle != null || colorBindings.Count > 0)
        //    {
        //        if(depthBinding.Handle == null && colorBindings.Count == 1)
        //        {
        //            command.SetRenderTarget(binding.colorRenderTargets[0]);
        //            //command.SetRenderTarget(binding.colorRenderTargets[0], binding.colorLoadActions[0], binding.colorStoreActions[0]);
        //        }
        //        else
        //        {
        //            command.SetRenderTarget(binding);
        //        }
        //    }

        //    if (!screenWrite)
        //        command.SetViewport(new Rect(0, 0, width, height));

        //    depthBinding = default;
        //    colorBindings.Clear();
        //    screenWrite = false;
        //}

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
