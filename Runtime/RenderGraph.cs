using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arycama.CustomRenderPipeline
{
    public class RenderGraph
    {
        private readonly Dictionary<Type, Queue<RenderPass>> renderPassPool = new();
        private readonly Dictionary<Type, Queue<RenderGraphBuilder>> builderPool = new();
        private readonly Dictionary<RenderTexture, RTHandle> importedTextures = new();
        private readonly Dictionary<GraphicsBuffer, BufferHandle> importedBuffers = new();

        private readonly List<RenderPass> renderPasses = new();

        // Maybe encapsulate these in a thing so it can also be used for buffers
        private readonly Queue<RTHandle> rtHandlePool = new();
        private readonly List<RTHandle> unavailableRtHandles = new();

        private readonly List<RenderTexture> availableRenderTextures = new();

        private readonly List<BufferHandle> bufferHandlesToCreate = new();
        private readonly List<BufferHandle> availableBufferHandles = new();
        private readonly List<BufferHandle> usedBufferHandles = new();

        private readonly Dictionary<RTHandle, int> checkSet = new();
        private readonly Dictionary<int, List<RTHandle>> lastPassOutputs = new();

        private bool isExecuting;

        private Dictionary<RTHandle, int> lastRtHandleRead = new();
        private Dictionary<int, List<RTHandle>> passRTHandleOutputs = new();
        private HashSet<RTHandle> writtenRTHandles = new();

        public BufferHandle EmptyBuffer { get; }
        public RTHandle EmptyTextureArray { get; }
        public RTHandle EmptyCubemapArray { get; }

        private int rtHandleCount;
        private int rtCount;
        private int screenWidth, screenHeight;

        public RenderGraph()
        {
            EmptyBuffer = ImportBuffer(new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int)));
            EmptyTextureArray = ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex2DArray, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave });
            EmptyCubemapArray = ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.CubeArray, volumeDepth = 6, hideFlags = HideFlags.HideAndDontSave });
        }

        public void SetScreenWidth(int width)
        {
            screenWidth = Mathf.Max(width, screenWidth);
        }

        public void SetScreenHeight(int height)
        {
            screenHeight = Mathf.Max(height, screenHeight);
        }

        public T AddRenderPass<T>(string name) where T : RenderPass, new()
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

            pass.Name = name;
            pass.Index = renderPasses.Count;

            return pass as T;
        }

        public void AddRenderPassInternal(RenderPass renderPass)
        {
            renderPasses.Add(renderPass);
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
            foreach (var bufferHandle in bufferHandlesToCreate)
                bufferHandle.Create();
            bufferHandlesToCreate.Clear();

            // Build mapping from pass index to rt handles that can be freed
            foreach (var input in lastRtHandleRead)
            {
                if (checkSet.ContainsKey(input.Key))
                {
                    Debug.LogError($"Trying to release rt handle in pass {input.Value} but it is already released in {checkSet[input.Key]}");
                    continue;
                }

                if (!lastPassOutputs.TryGetValue(input.Value, out var list))
                {
                    list = new();
                    lastPassOutputs.Add(input.Value, list);
                }

                list.Add(input.Key);
            }

            for (var i = 0; i < renderPasses.Count; i++)
            {
                var renderPass = renderPasses[i];

                // Assign or create any RTHandles that are written to by this pass
                if (passRTHandleOutputs.TryGetValue(i, out var outputs))
                {
                    foreach (var handle in outputs)
                    {
                        // Ignore imported textures
                        if (handle.IsImported)
                            continue;

                        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
                        RenderTexture result = null;
                        for (var j = 0; j < availableRenderTextures.Count; j++)
                        {
                            var rt = availableRenderTextures[j];

                            Assert.IsNotNull(rt);

                            var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
                            if ((isDepth && handle.Format != rt.depthStencilFormat) || (!isDepth && handle.Format != rt.graphicsFormat))
                                continue;

                            // For screen textures, ensure we get a rendertexture that is the actual screen width/height
                            if (handle.IsScreenTexture)
                            {
                                if (rt.width != screenWidth || rt.height != screenHeight)
                                    continue;
                            }
                            else if (rt.width < handle.Width || rt.height < handle.Height)
                                continue;

                            if (rt.enableRandomWrite == handle.EnableRandomWrite && rt.dimension == handle.Dimension)
                            {
                                if (handle.Dimension != TextureDimension.Tex2D && rt.volumeDepth < handle.VolumeDepth)
                                    continue;

                                result = rt;
                                availableRenderTextures.RemoveAt(j);
                                break;
                            }
                        }

                        if (result == null)
                        {
                            var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);

                            var width = handle.IsScreenTexture ? screenWidth : handle.Width;
                            var height = handle.IsScreenTexture ? screenHeight : handle.Height;

                            result = new RenderTexture(width, height, isDepth ? GraphicsFormat.None : handle.Format, isDepth ? handle.Format : GraphicsFormat.None) { enableRandomWrite = handle.EnableRandomWrite, hideFlags = HideFlags.HideAndDontSave };

                            if (handle.VolumeDepth > 0)
                            {
                                result.dimension = handle.Dimension;
                                result.volumeDepth = handle.VolumeDepth;
                                result.useMipMap = handle.HasMips;
                            }

                            result.name = $"RTHandle {rtCount++} {result.dimension} {result.graphicsFormat} {width}x{height} ";
                            result.Create();
                        }

                        handle.RenderTexture = result;
                        Assert.IsNotNull(result);
                    }
                }

                // Release any textures if this was their final read
                if (lastPassOutputs.TryGetValue(i, out var outputsToFree))
                {
                    for (var i1 = 0; i1 < outputsToFree.Count; i1++)
                    {
                        var output = outputsToFree[i1];

                        if (output.IsImported)
                            continue;

                        availableRenderTextures.Add(output.RenderTexture);
                    }
                }
            }

            isExecuting = true;
            try
            {
                foreach(var renderPass in renderPasses)
                    renderPass.Run(command, context);
            }
            finally
            {
                isExecuting = false;

                // Release all pooled passes
                foreach (var pass in renderPasses)
                {
                    var hasPool = renderPassPool.TryGetValue(pass.GetType(), out var pool);
                    Assert.IsTrue(hasPool, "Attempting to release a renderPass that was not created through GetPool");
                    pool.Enqueue(pass);
                }

                renderPasses.Clear();
                lastRtHandleRead.Clear();
                writtenRTHandles.Clear();

                foreach(var list in passRTHandleOutputs)
                {
                    list.Value.Clear();
                }

                checkSet.Clear();
                foreach(var output in lastPassOutputs)
                {
                    output.Value.Clear();
                }
            }
        }

        public RTHandle GetTexture(int width, int height, GraphicsFormat format, bool enableRandomWrite = false, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false)
        {
            // Ensure we're not getting a texture during execution, this must be done in the setup
            Assert.IsFalse(isExecuting);

            if (!rtHandlePool.TryDequeue(out var result))
            {
                result = new RTHandle();
                result.Id = rtHandleCount++;
            }

            result.Width = width;
            result.Height = height;
            result.Format = format;
            result.EnableRandomWrite = enableRandomWrite;
            result.VolumeDepth = volumeDepth;
            result.Dimension = dimension;
            result.IsScreenTexture = isScreenTexture;
            result.HasMips = hasMips;

            unavailableRtHandles.Add(result);
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

                if (handle.Target != target)
                    continue;

                if (handle.Stride != stride)
                    continue;

                if (handle.Target.HasFlag(GraphicsBuffer.Target.Constant))
                {
                    // Constant buffers must have exact size
                    if (handle.Count != count)
                        continue;
                }
                else if (handle.Count < count)
                    continue;

                handle.Size = count * stride;
                availableBufferHandles.RemoveAt(i);
                usedBufferHandles.Add(handle);
                return handle;
            }

            // If no handle was found, create a new one, and assign it as one to be created. 
            var result = new BufferHandle(target, count, stride);
            result.Size = count * stride;
            bufferHandlesToCreate.Add(result);
            usedBufferHandles.Add(result);
            return result;
        }

        public RTHandle ImportRenderTexture(RenderTexture texture)
        {
            if (!importedTextures.TryGetValue(texture, out var result))
            {
                result = (RTHandle)texture;
                result.Id = rtHandleCount++;
                importedTextures.Add(texture, result);
                result.IsImported = true;
                result.IsScreenTexture = false;
            }

            return result;
        }

        public void ReleaseImportedTexture(RenderTexture texture)
        {
            var wasRemoved = importedTextures.Remove(texture);
            Assert.IsTrue(wasRemoved, "Trying to release a non-imported texture");
        }

        public BufferHandle ImportBuffer(GraphicsBuffer buffer)
        {
            if (!importedBuffers.TryGetValue(buffer, out var result))
            {
                result = (BufferHandle)buffer;
                importedBuffers.Add(buffer, result);
            }

            return result;
        }

        public void ReleaseImportedBuffer(GraphicsBuffer buffer)
        {
            var wasRemoved = importedBuffers.Remove(buffer);
            Assert.IsTrue(wasRemoved, "Trying to release a non-imported buffer");
        }

        public void ReleaseHandles()
        {
            // Any handles that were not used this frame can be removed
            foreach (var bufferHandle in availableBufferHandles)
                bufferHandle.Release();
            availableBufferHandles.Clear();

            // Mark all handles as available for use again
            foreach (var handle in unavailableRtHandles)
                rtHandlePool.Enqueue(handle);
            unavailableRtHandles.Clear();

            foreach (var handle in usedBufferHandles)
                availableBufferHandles.Add(handle);
            usedBufferHandles.Clear();
        }

        public void Release()
        {
            foreach (var rt in availableRenderTextures)
                Object.DestroyImmediate(rt);
            availableRenderTextures.Clear();

            foreach (var bufferHandle in availableBufferHandles)
                bufferHandle.Release();
            availableBufferHandles.Clear();

            EmptyBuffer.Release();
        }

        public void SetRTHandleWrite(RTHandle handle, int passIndex)
        {
            if (!writtenRTHandles.Add(handle))
                return;

            if (!passRTHandleOutputs.TryGetValue(passIndex, out var outputs))
            {
                outputs = new List<RTHandle>();
                passRTHandleOutputs.Add(passIndex, outputs);
            }

            outputs.Add(handle);
        }

        public void SetLastRTHandleRead(RTHandle handle, int passIndex)
        {
            lastRtHandleRead[handle] = passIndex;
        }
    }
}
