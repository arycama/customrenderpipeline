using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public delegate void RenderGraphPass(CommandBuffer command, ScriptableRenderContext context);

    public class RenderGraph
    {
        private List<RenderPass> actions = new();
        private List<RTHandle> handlesToCreate = new();
        private List<RTHandle> availableHandles = new();
        private List<RTHandle> usedHandles = new();

        readonly ObjectPool<MaterialPropertyBlock> propertyBlockPool = new(() => new MaterialPropertyBlock(), x => x.Clear());

        private bool isExecuting;

        public MaterialPropertyBlock GetPropertyBlock()
        {
            return propertyBlockPool.Get();
        }

        public void ReleasePropertyBlock(MaterialPropertyBlock propertyBlock)
        {
            propertyBlockPool.Release(propertyBlock);
        }

        public T AddRenderPass<T>() where T : RenderPass, new()
        {
            var builder = new T();
            actions.Add(builder);
            return builder;
        }

        public T AddRenderPass<T>(T renderPass) where T : RenderPass
        {
            actions.Add(renderPass);
            return renderPass;
        }

        public void Execute(CommandBuffer command, ScriptableRenderContext context)
        {
            // Create all RTs.
            foreach (var handle in handlesToCreate)
            {
                handle.Create();
            }

            handlesToCreate.Clear();

            isExecuting = true;
            try
            {
                foreach (var action in actions)
                {
                    action.Run(command, context);
                }
            }
            finally
            {
                isExecuting = false;
            }

            actions.Clear();
        }

        public RTHandle GetTexture(int width, int height, GraphicsFormat format, bool enableRandomWrite = false, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D)
        {
            // Ensure we're not getting a texture during execution, this must be done in the setup
            Assert.IsFalse(isExecuting);

            // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
            for (var i = 0; i < availableHandles.Count; i++)
            {
                var handle = availableHandles[i];
                if (handle.Width == width && handle.Height == height && handle.Format == format && handle.EnableRandomWrite == enableRandomWrite && handle.VolumeDepth == volumeDepth && handle.Dimension == dimension)
                {
                    availableHandles.RemoveAt(i);
                    usedHandles.Add(handle);
                    return handle;
                }
            }

            // If no handle was found, create a new one, and assign it as one to be created. 
            var result = new RTHandle(width, height, format, enableRandomWrite, volumeDepth, dimension);
            handlesToCreate.Add(result);
            usedHandles.Add(result);
            return result;
        }

        public void ReleaseRTHandles()
        {
            // Any handles that were not used this frame can be removed
            foreach (var handle in availableHandles)
                handle.Release();

            availableHandles.Clear();

            // Mark all RTHandles as available for use again
            foreach (var handle in usedHandles)
                availableHandles.Add(handle);

            usedHandles.Clear();
        }

        public void Release()
        {
            foreach (var handle in availableHandles)
                handle.Release();

            availableHandles.Clear();
        }
    }
}

