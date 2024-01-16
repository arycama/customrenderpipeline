using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class RenderGraph
    {
        private Dictionary<Type, Queue<RenderPass>> renderPassPool = new();
        private Dictionary<Type, Queue<RenderGraphBuilder>> builderPool = new();

        private readonly List<RenderPass> renderPasses = new();

        // Maybe encapsulate these in a thing so it can also be used for buffers
        private readonly List<RTHandle> rtHandlesToCreate = new();
        private readonly List<RTHandle> availableRtHandles = new();
        private readonly List<RTHandle> usedRtHandles = new();

        private readonly List<BufferHandle> bufferHandlesToCreate = new();
        private readonly List<BufferHandle> availableBufferHandles = new();
        private readonly List<BufferHandle> usedBufferHandles = new();

        private bool isExecuting;
        private GraphicsBuffer emptyBuffer;

        public RenderGraph()
        {
            emptyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int));
        }

        public T AddRenderPass<T>() where T : RenderPass, new()
        {
            if (!renderPassPool.TryGetValue(typeof(T), out var pool))
            {
                pool = new Queue<RenderPass>();
                renderPassPool.Add(typeof(T), pool);
            }

            if (!pool.TryDequeue(out var pass))
            {
                pass = new T();
                pass.RenderGraph = this;
            }

            renderPasses.Add(pass);
            return pass as T;
        }

        public RenderGraphBuilder<T> GetRenderGraphBuilder<T>() where T : class, new()
        {
            if (!builderPool.TryGetValue(typeof(RenderGraphBuilder<T>), out var pool))
            {
                pool = new Queue<RenderGraphBuilder>();
                builderPool.Add(typeof(RenderGraphBuilder<T>), pool);
            }

            if (!pool.TryDequeue(out var pass))
                pass = new RenderGraphBuilder<T>();

            return pass as RenderGraphBuilder<T>;
        }

        public void ReleaseRenderGraphBuilder(RenderGraphBuilder builder)
        {
            var hasPool = builderPool.TryGetValue(builder.GetType(), out var pool);
            Assert.IsTrue(hasPool, "Attempting to release a renderPass that was not created through GetPool");
            pool.Enqueue(builder);
        }

        public void Execute(CommandBuffer command, ScriptableRenderContext context)
        {
            // Create all RTs.
            foreach (var rtHandle in rtHandlesToCreate)
                rtHandle.Create();
            rtHandlesToCreate.Clear();

            foreach (var bufferHandle in bufferHandlesToCreate)
                bufferHandle.Create();
            bufferHandlesToCreate.Clear();

            isExecuting = true;
            try
            {
                foreach (var renderPass in renderPasses)
                {
                    renderPass.Run(command, context);
                }
            }
            finally
            {
                isExecuting = false;
            }

            // Release all pooled passes
            foreach(var pass in renderPasses)
            {
                var hasPool = renderPassPool.TryGetValue(pass.GetType(), out var pool);
                Assert.IsTrue(hasPool, "Attempting to release a renderPass that was not created through GetPool");
                pool.Enqueue(pass);
            }

            renderPasses.Clear();
        }

        public RTHandle GetTexture(int width, int height, GraphicsFormat format, bool enableRandomWrite = false, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D)
        {
            // Ensure we're not getting a texture during execution, this must be done in the setup
            Assert.IsFalse(isExecuting);

            // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
            for (var i = 0; i < availableRtHandles.Count; i++)
            {
                var handle = availableRtHandles[i];
                if (handle.Width == width && handle.Height == height && handle.Format == format && handle.EnableRandomWrite == enableRandomWrite && handle.VolumeDepth == volumeDepth && handle.Dimension == dimension)
                {
                    availableRtHandles.RemoveAt(i);
                    usedRtHandles.Add(handle);
                    return handle;
                }
            }

            // If no handle was found, create a new one, and assign it as one to be created. 
            var result = new RTHandle(width, height, format, enableRandomWrite, volumeDepth, dimension);
            rtHandlesToCreate.Add(result);
            usedRtHandles.Add(result);
            return result;
        }

        public BufferHandle GetBuffer(int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured)
        {
            Assert.IsTrue(count > 0);
            Assert.IsTrue(stride > 0);

            // Ensure we're not getting a texture during execution, this must be done in the setup
            Assert.IsFalse(isExecuting);

            // Find first matching buffer (TODO: Allow returning buffer smaller than required)
            for (var i = 0; i < availableBufferHandles.Count; i++)
            {
                var handle = availableBufferHandles[i];
                if (handle.Target == target && handle.Stride == stride && handle.Count >= count)
                {
                    handle.Size = count;
                    availableBufferHandles.RemoveAt(i);
                    usedBufferHandles.Add(handle);
                    return handle;
                }
            }

            // If no handle was found, create a new one, and assign it as one to be created. 
            var result = new BufferHandle(target, count, stride);
            result.Size = count;
            bufferHandlesToCreate.Add(result);
            usedBufferHandles.Add(result);
            return result;
        }

        public GraphicsBuffer GetEmptyBuffer()
        {
            return emptyBuffer;
        }

        public void ReleaseHandles()
        {
            // Any handles that were not used this frame can be removed
            foreach (var handle in availableRtHandles)
                handle.Release();
            availableRtHandles.Clear();

            foreach (var bufferHandle in availableBufferHandles)
                bufferHandle.Release();
            availableBufferHandles.Clear();

            // Mark all handles as available for use again
            foreach (var handle in usedRtHandles)
                availableRtHandles.Add(handle);
            usedRtHandles.Clear();

            foreach (var handle in usedBufferHandles)
                availableBufferHandles.Add(handle);
            usedBufferHandles.Clear();
        }

        public void Release()
        {
            foreach (var handle in availableRtHandles)
                handle.Release();
            availableRtHandles.Clear();

            foreach(var bufferHandle in availableBufferHandles) 
                bufferHandle.Release();
            availableBufferHandles.Clear();

            emptyBuffer.Release();
        }
    }
}
