using System;
using System.Collections.Generic;
using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderPass
    {
        protected RenderGraphBuilder renderGraphBuilder;

        protected bool screenWrite;
        protected RTHandleBindingData depthBinding;
        protected readonly List<RTHandleBindingData> colorBindings = new();
        private readonly List<(string, RTHandle)> readTextures = new();
        private readonly List<(string, BufferHandle)> readBuffers = new();
        private readonly List<(string, BufferHandle)> writeBuffers = new();

        public RenderGraph RenderGraph { get; set; }

        public abstract void SetTexture(CommandBuffer command, string propertyName, Texture texture);
        public abstract void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer);
        public abstract void SetVector(CommandBuffer command, string propertyName, Vector4 value);
        public abstract void SetFloat(CommandBuffer command, string propertyName, float value);
        public abstract void SetInt(CommandBuffer command, string propertyName, int value);
        protected abstract void Execute(CommandBuffer command);

        public void ReadTexture(string propertyName, RTHandle texture)
        {
            readTextures.Add((propertyName, texture));
        }

        public void WriteScreen()
        {
            screenWrite = true;
        }

        public void WriteTexture(string propertyName, RTHandle handle, RenderBufferLoadAction loadAction = RenderBufferLoadAction.DontCare, RenderBufferStoreAction storeAction = RenderBufferStoreAction.DontCare, Color clearColor = default)
        {
            colorBindings.Add(new RTHandleBindingData(handle, loadAction, storeAction, clearColor, nameId: Shader.PropertyToID(propertyName)));
        }

        public void WriteDepth(string propertyName, RTHandle handle, RenderBufferLoadAction loadAction = RenderBufferLoadAction.DontCare, RenderBufferStoreAction storeAction = RenderBufferStoreAction.DontCare, float clearDepth = 1.0f, RenderTargetFlags flags = RenderTargetFlags.None)
        {
            depthBinding = new(handle, loadAction, storeAction, default, clearDepth, flags);
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
            foreach (var texture in readTextures)
                SetTexture(command, texture.Item1, texture.Item2);
            readTextures.Clear();

            foreach (var buffer in readBuffers)
                SetBuffer(command, buffer.Item1, buffer.Item2);
            readBuffers.Clear();

            foreach (var buffer in writeBuffers)
                SetBuffer(command, buffer.Item1, buffer.Item2);
            writeBuffers.Clear();

            SetupTargets(command);

            if (renderGraphBuilder != null)
            {
                renderGraphBuilder.Execute(command, context);
                renderGraphBuilder.ClearRenderFunction();
            }

            Execute(command);

            if (renderGraphBuilder != null)
            {
                RenderGraph.ReleaseRenderGraphBuilder(renderGraphBuilder);
                renderGraphBuilder = null;
            }
        }

        protected virtual void SetupTargets(CommandBuffer command)
        {
            // TODO: Can clear a depth and color target together
            var binding = new RenderTargetBinding();
            if (depthBinding.Handle != null)
            {
                // Load action not supported outside of renderpass API, so emulate it here
                if (depthBinding.LoadAction == RenderBufferLoadAction.Clear)
                {
                    command.SetRenderTarget(BuiltinRenderTextureType.None, depthBinding.Handle);
                    command.ClearRenderTarget(true, false, default, depthBinding.ClearDepth);
                    binding.depthLoadAction = RenderBufferLoadAction.Load;
                }
                else
                {
                    binding.depthLoadAction = depthBinding.LoadAction;
                }

                binding.depthRenderTarget = depthBinding.Handle;
                binding.depthStoreAction = depthBinding.StoreAction;
                binding.flags = depthBinding.Flags;
            }
            else
            {
                // Need to set binding.depthRenderTarget to BuiltinRenderTextureType.None or the pass won't work
                binding.depthRenderTarget = BuiltinRenderTextureType.None;
                binding.depthLoadAction = RenderBufferLoadAction.DontCare;
            }

            if (colorBindings.Count > 0)
            {
                var targets = new RenderTargetIdentifier[colorBindings.Count];
                var loadActions = new RenderBufferLoadAction[colorBindings.Count];
                var storeActions = new RenderBufferStoreAction[colorBindings.Count];

                for (var i = 0; i < colorBindings.Count; i++)
                {
                    var target = colorBindings[i];

                    // Load action not supported outside of renderpass API, so emulate it here
                    if (target.LoadAction == RenderBufferLoadAction.Clear)
                    {
                        command.SetRenderTarget(target.Handle);
                        command.ClearRenderTarget(false, true, target.ClearColor);
                        loadActions[i] = RenderBufferLoadAction.Load;
                    }
                    else
                    {
                        loadActions[i] = target.LoadAction;
                    }

                    targets[i] = target.Handle;
                    storeActions[i] = target.StoreAction;
                }

                binding.colorRenderTargets = targets;
                binding.colorLoadActions = loadActions;
                binding.colorStoreActions = storeActions;
            }

            if (screenWrite)
                command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            else if (depthBinding.Handle != null || colorBindings.Count > 0)
                command.SetRenderTarget(binding);

            depthBinding = default;
            colorBindings.Clear();
        }

        public void SetRenderFunction(Action<CommandBuffer, ScriptableRenderContext> pass)
        {
            var result = RenderGraph.GetRenderGraphBuilder();
            result.SetRenderFunction(pass);
            renderGraphBuilder = result;
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

    public virtual void Execute(CommandBuffer command, ScriptableRenderContext context)
    {
        pass?.Invoke(command, context);
    }
}

public class RenderGraphBuilder<T> : RenderGraphBuilder where T : class, new()
{
    public T Data { get; } = new();
    private Action<CommandBuffer, ScriptableRenderContext, T> pass;

    public void SetRenderFunction(Action<CommandBuffer, ScriptableRenderContext, T> pass)
    {
        this.pass = pass;
    }

    public override void ClearRenderFunction()
    {
        pass = null;
    }

    public override void Execute(CommandBuffer command, ScriptableRenderContext context)
    {
        pass?.Invoke(command, context, Data);
    }
}

public struct ScopedRenderPass<T> : IDisposable where T : RenderPass, new()
{
    private readonly RenderGraph renderGraph;

    public ScopedRenderPass(RenderGraph renderGraph, T renderPass)
    {
        this.renderGraph = renderGraph;
        RenderPass = renderPass;
    }

    public T RenderPass { get; }

    void IDisposable.Dispose()
    {
        renderGraph.AddRenderPassInternal(RenderPass);
    }
}