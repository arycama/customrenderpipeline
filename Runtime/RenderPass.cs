using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderPass
    {
        protected RenderGraphBuilder renderGraphBuilder;

        private RTHandleBindingData depthBinding;
        private readonly List<RTHandleBindingData> colorBindings = new();
        private readonly List<(string, RTHandle)> readTextures = new();
        private readonly List<(string, BufferHandle)> readBuffers = new();
        private readonly List<(string, BufferHandle)> writeBuffers = new();

        public RenderGraph RenderGraph { get; set; }

        public abstract void SetTexture(CommandBuffer command, string propertyName, Texture texture);
        public abstract void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer);
        public abstract void SetVector(CommandBuffer command, string propertyName, Vector4 value);
        public abstract void SetFloat(CommandBuffer command, string propertyName, float value);
        public abstract void SetInt(CommandBuffer command, string propertyName, int value);
        public abstract void Execute(CommandBuffer command);

        public void ReadTexture(string propertyName, RTHandle texture)
        {
            readTextures.Add((propertyName, texture));
        }

        public void WriteTexture(string propertyName, RTHandle handle, RenderBufferLoadAction loadAction = RenderBufferLoadAction.DontCare, RenderBufferStoreAction storeAction = RenderBufferStoreAction.DontCare, Color clearColor = default)
        {
            colorBindings.Add(new RTHandleBindingData(handle, loadAction, storeAction, clearColor));
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

            if (depthBinding.Handle != null || colorBindings.Count > 0)
                command.SetRenderTarget(binding);

            depthBinding = default;
            colorBindings.Clear();

            renderGraphBuilder.Execute(command, context);
            RenderGraph.ReleaseRenderGraphBuilder(renderGraphBuilder);
            renderGraphBuilder = null;
        }

        public T SetRenderFunction<T>(Action<CommandBuffer, ScriptableRenderContext, T> pass) where T : class, new()
        {
            var result = RenderGraph.GetRenderGraphBuilder<T>();
            result.SetRenderFunction(pass);
            renderGraphBuilder = result;
            return result.Data;
        }
    }
}

public abstract class RenderGraphBuilder
{ 
    public abstract void Execute(CommandBuffer command, ScriptableRenderContext context);
}

public class RenderGraphBuilder<T> : RenderGraphBuilder where T : class, new()
{
    public T Data { get; } = new();
    private Action<CommandBuffer, ScriptableRenderContext, T> pass;

    public void SetRenderFunction(Action<CommandBuffer, ScriptableRenderContext, T> pass)
    {
        this.pass = pass;
    }

    public override void Execute(CommandBuffer command, ScriptableRenderContext context)
    {
        pass.Invoke(command, context, Data);
    }
}