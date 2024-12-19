using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arycama.CustomRenderPipeline
{
    public class RenderGraph : IDisposable
    {
        const int swapChainCount = 3;

        private readonly Dictionary<Type, Queue<RenderPass>> renderPassPool = new();
        private readonly Dictionary<Type, Queue<RenderGraphBuilder>> builderPool = new();
        private readonly Dictionary<RenderTexture, RTHandle> importedTextures = new();
        private readonly Dictionary<GraphicsBuffer, BufferHandle> importedBuffers = new();

        private readonly List<RenderPass> renderPasses = new();

        // Maybe encapsulate these in a thing so it can also be used for buffers
        private readonly Queue<RTHandle> availableRtHandles = new();
        private readonly Queue<int> availableRtSlots = new();
        private readonly List<(RenderTexture renderTexture, int lastFrameUsed, bool isAvailable, bool isPersistent)> availableRenderTextures = new();

        private readonly List<BufferHandle> bufferHandlesToCreate = new();
        private readonly List<(BufferHandle handle, int lastFrameUsed)> availableBufferHandles = new();
        private readonly List<BufferHandle> usedBufferHandles = new();

        private readonly Dictionary<int, List<RTHandle>> lastPassOutputs = new();

        public bool IsExecuting { get; private set; }

        private readonly Dictionary<RTHandle, int> lastRtHandleRead = new();
        private readonly Dictionary<int, List<RTHandle>> passRTHandleOutputs = new();
        private readonly HashSet<RTHandle> writtenRTHandles = new();

        private readonly HashSet<RenderTexture> allRenderTextures = new();

        public BufferHandle EmptyBuffer { get; }
        public RTHandle EmptyTexture { get; }
        public RTHandle EmptyUavTexture { get; }
        public RTHandle EmptyTextureArray { get; }
        public RTHandle Empty3DTexture { get; }
        public RTHandle EmptyCubemap { get; }
        public RTHandle EmptyCubemapArray { get; }

        public int FrameIndex { get; private set; }

        public RenderResourceMap ResourceMap { get; }
        public CustomRenderPipeline RenderPipeline { get; }

        private int rtHandleCount;
        private int rtCount;
        private int screenWidth, screenHeight;
        private bool disposedValue;

        public RenderGraph(CustomRenderPipeline renderPipeline)
        {
            EmptyBuffer = ImportBuffer(new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int)) { name = "Empty Structured Buffer" });
            EmptyTexture = ImportRenderTexture(new RenderTexture(1, 1, 0) { hideFlags = HideFlags.HideAndDontSave });
            EmptyUavTexture = ImportRenderTexture(new RenderTexture(1, 1, 0) { hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = true });
            EmptyTextureArray = ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex2DArray, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave });
            Empty3DTexture = ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex3D, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave });
            EmptyCubemap = ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Cube, hideFlags = HideFlags.HideAndDontSave });
            EmptyCubemapArray = ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.CubeArray, volumeDepth = 6, hideFlags = HideFlags.HideAndDontSave });

            ResourceMap = new(this);
            RenderPipeline = renderPipeline;

            allRenderTextures.Add(EmptyTexture.RenderTexture);
            allRenderTextures.Add(EmptyUavTexture.RenderTexture);
            allRenderTextures.Add(EmptyTextureArray.RenderTexture);
            allRenderTextures.Add(Empty3DTexture.RenderTexture);
            allRenderTextures.Add(EmptyCubemap.RenderTexture);
            allRenderTextures.Add(EmptyCubemapArray.RenderTexture);
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
            var pool = renderPassPool.GetOrAdd(typeof(T));

            if (!pool.TryDequeue(out var pass))
            {
                pass = new T
                {
                    RenderGraph = this
                };
            }

            pass.Name = name;
            pass.Index = renderPasses.Count;

            return pass as T;
        }

        public void AddRenderPassInternal(RenderPass renderPass)
        {
            renderPasses.Add(renderPass);
        }

        public RenderGraphBuilder GetRenderGraphBuilder()
        {
            var pool = builderPool.GetOrAdd(typeof(RenderGraphBuilder));
            if (!pool.TryDequeue(out var value))
                value = new RenderGraphBuilder();

            return value;
        }

        public RenderGraphBuilder<T> GetRenderGraphBuilder<T>()
        {
            var pool = builderPool.GetOrAdd(typeof(RenderGraphBuilder<T>));
            if (!pool.TryDequeue(out var value))
                value = new RenderGraphBuilder<T>();

            return value as RenderGraphBuilder<T>;
        }

        public void ReleaseRenderGraphBuilder(RenderGraphBuilder builder)
        {
            builderPool[builder.GetType()].Enqueue(builder);
        }

        public void Execute(CommandBuffer command)
        {
            foreach (var bufferHandle in bufferHandlesToCreate)
                bufferHandle.Create();

            // Build mapping from pass index to rt handles that can be freed
            foreach (var input in lastRtHandleRead)
            {
                var list = lastPassOutputs.GetOrAdd(input.Value);
                list.Add(input.Key);
            }

            for (var i = 0; i < renderPasses.Count; i++)
            {
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
                            var (renderTexture, lastFrameUsed, isAvailable, isPersistent) = availableRenderTextures[j];
                            if (!isAvailable)
                                continue;

                            var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
                            Assert.IsNotNull(handle, "Handle is null in pass");
                            Assert.IsNotNull(renderTexture, "renderTexture is null in pass");
                            if ((isDepth && handle.Format != renderTexture.depthStencilFormat) || (!isDepth && handle.Format != renderTexture.graphicsFormat))
                                continue;

                            // TODO: Use some enum instead?
                            if (handle.IsExactSize)
                            {
                                if (renderTexture.width != handle.Width || renderTexture.height != handle.Height)
                                    continue;
                            }
                            else if (handle.IsScreenTexture)
                            {
                                // For screen textures, ensure we get a rendertexture that is the actual screen width/height
                                if (renderTexture.width != screenWidth || renderTexture.height != screenHeight)
                                    continue;
                            }
                            else if (renderTexture.width < handle.Width || renderTexture.height < handle.Height)
                                continue;

                            if (renderTexture.enableRandomWrite == handle.EnableRandomWrite && renderTexture.dimension == handle.Dimension && renderTexture.useMipMap == handle.HasMips)
                            {
                                if (handle.Dimension != TextureDimension.Tex2D && renderTexture.volumeDepth < handle.VolumeDepth)
                                    continue;

                                result = renderTexture;
                                Assert.IsNotNull(renderTexture);
                                Assert.IsTrue(renderTexture.IsCreated());
                                availableRenderTextures[j] = (renderTexture, lastFrameUsed, false, handle.IsPersistent);
                                handle.RenderTextureIndex = j;
                                break;
                            }
                        }

                        if (result == null)
                        {
                            var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
                            var isStencil = handle.Format == GraphicsFormat.D32_SFloat_S8_UInt || handle.Format == GraphicsFormat.D24_UNorm_S8_UInt;

                            var width = handle.IsScreenTexture ? screenWidth : handle.Width;
                            var height = handle.IsScreenTexture ? screenHeight : handle.Height;

                            result = new RenderTexture(width, height, isDepth ? GraphicsFormat.None : handle.Format, isDepth ? handle.Format : GraphicsFormat.None) { enableRandomWrite = handle.EnableRandomWrite, stencilFormat = isStencil ? GraphicsFormat.R8_UInt : GraphicsFormat.None, hideFlags = HideFlags.HideAndDontSave };
                            allRenderTextures.Add(result);

                            if (handle.VolumeDepth > 0)
                            {
                                result.dimension = handle.Dimension;
                                result.volumeDepth = handle.VolumeDepth;
                                result.useMipMap = handle.HasMips;
                                result.autoGenerateMips = false; // Always false, we manually handle mip generation if needed
                            }

                            result.name = $"{result.dimension} {(isDepth ? result.depthStencilFormat : result.graphicsFormat)} {width}x{height} {rtCount++}";
                            _ = result.Create();

                            //Debug.Log($"Allocating {result.name}");

                            // Get a slot for this render texture if possible
                            if (!availableRtSlots.TryDequeue(out var slot))
                            {
                                slot = availableRenderTextures.Count;
                                Assert.IsNotNull(result);
                                availableRenderTextures.Add((result, FrameIndex, false, handle.IsPersistent));
                            }
                            else
                            {
                                Assert.IsNotNull(result);
                                availableRenderTextures[slot] = (result, FrameIndex, false, handle.IsPersistent);
                            }

                            handle.RenderTextureIndex = slot;
                        }

                        handle.RenderTexture = result;
                    }
                }

                // Release any textures if this was their final read
                if (!lastPassOutputs.TryGetValue(i, out var outputsToFree))
                    continue;

                foreach (var output in outputsToFree)
                {
                    if (output.IsImported)
                        continue;

                    availableRenderTextures[output.RenderTextureIndex] = (output.RenderTexture, FrameIndex, true, false);
                    availableRtHandles.Enqueue(output);
                }
            }

            IsExecuting = true;

            foreach (var renderPass in renderPasses)
                renderPass.Run(command);

            IsExecuting = false;
        }

        public RTHandle GetTexture(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false, bool isExactSize = false)
        {
            // Ensure we're not getting a texture during execution, this must be done in the setup
            Assert.IsFalse(IsExecuting);

            if (!availableRtHandles.TryDequeue(out var result))
            {
                result = new RTHandle
                {
                    Id = rtHandleCount++
                };
            }

            result.Width = width;
            result.Height = height;
            result.Format = format;
            result.VolumeDepth = volumeDepth;
            result.Dimension = dimension;
            result.IsScreenTexture = isScreenTexture;
            result.HasMips = hasMips;
            result.AutoGenerateMips = autoGenerateMips;
            result.IsPersistent = isPersistent;
            result.IsAssigned = isPersistent ? false : true;
            result.IsExactSize = isExactSize;

            // This gets set automatically if a texture is written to by a compute shader
            result.EnableRandomWrite = false;

            return result;
        }

        public BufferHandle GetBuffer(int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
        {
            Assert.IsTrue(count > 0);
            Assert.IsTrue(stride > 0);

            // Ensure we're not getting a texture during execution, this must be done in the setup
            Assert.IsFalse(IsExecuting);

            // Find first matching buffer (TODO: Allow returning buffer smaller than required)
            for (var i = 0; i < availableBufferHandles.Count; i++)
            {
                var handle = availableBufferHandles[i];

                // If this buffer can be written to directly, it must have been unused for at least two frames, otherwise it will write to a temp buffer and results
                // will not be visible until the next frame.
                if (usageFlags == GraphicsBuffer.UsageFlags.LockBufferForWrite && handle.lastFrameUsed + (swapChainCount - 1) >= FrameIndex)
                    continue;

                if (handle.handle.Target != target)
                    continue;

                if (handle.handle.Stride != stride)
                    continue;

                if (handle.handle.UsageFlags != usageFlags)
                    continue;

                if (handle.handle.Target.HasFlag(GraphicsBuffer.Target.Constant))
                {
                    // Constant buffers must have exact size
                    if (handle.handle.Count != count)
                        continue;
                }
                else if (handle.handle.Count < count)
                    continue;

                handle.handle.Size = count * stride;
                availableBufferHandles.RemoveAt(i);
                usedBufferHandles.Add(handle.handle);
                return handle.handle;
            }

            // If no handle was found, create a new one, and assign it as one to be created. 
            var result = new BufferHandle(target, count, stride, usageFlags)
            {
                Size = count * stride
            };
            bufferHandlesToCreate.Add(result);
            usedBufferHandles.Add(result);
            return result;
        }

        public RTHandle ImportRenderTexture(RenderTexture renderTexture, bool autoGenerateMips = false)
        {
            if (importedTextures.TryGetValue(renderTexture, out var result))
                return result;

            // Ensure its created (Can happen with some RenderTextures that are imported as soon as created
            if (!renderTexture.IsCreated())
                _ = renderTexture.Create();

            result = new RTHandle
            {
                Width = renderTexture.width,
                Height = renderTexture.height,
                Format = renderTexture.graphicsFormat,
                EnableRandomWrite = renderTexture.enableRandomWrite,
                VolumeDepth = renderTexture.volumeDepth,
                Dimension = renderTexture.dimension,
                RenderTexture = renderTexture,
                HasMips = renderTexture.useMipMap,
                AutoGenerateMips = autoGenerateMips,
                Id = rtHandleCount++
            };
            importedTextures.Add(renderTexture, result);
            result.IsImported = true;
            result.IsScreenTexture = false;
            result.IsAssigned = true;
            result.IsExactSize = true;

            return result;
        }

        public void ReleaseImportedTexture(RenderTexture texture)
        {
            var wasRemoved = importedTextures.Remove(texture);
            Assert.IsTrue(wasRemoved, "Trying to release a non-imported texture");
        }

        public BufferHandle ImportBuffer(GraphicsBuffer buffer)
        {
            return importedBuffers.GetOrAdd(buffer, () => new BufferHandle(buffer));
        }

        public void ReleaseImportedBuffer(GraphicsBuffer buffer)
        {
            var wasRemoved = importedBuffers.Remove(buffer);
            Assert.IsTrue(wasRemoved, "Trying to release a non-imported buffer");
        }

        public void CleanupCurrentFrame()
        {
            // Release all pooled passes
            foreach (var pass in renderPasses)
                renderPassPool[pass.GetType()].Enqueue(pass);

            bufferHandlesToCreate.Clear();
            renderPasses.Clear();
            lastRtHandleRead.Clear();
            writtenRTHandles.Clear();

            foreach (var list in passRTHandleOutputs)
                list.Value.Clear();

            foreach (var output in lastPassOutputs)
                output.Value.Clear();

            // Any handles that were not used this frame can be removed
            foreach (var bufferHandle in availableBufferHandles)
            {
                // Keep buffers available for at least two frames
                if (bufferHandle.lastFrameUsed + (swapChainCount - 1) < FrameIndex)
                    bufferHandle.handle.Dispose();
            }

            availableBufferHandles.Clear();

            foreach (var handle in usedBufferHandles)
                availableBufferHandles.Add((handle, FrameIndex));
            usedBufferHandles.Clear();

            // Release any render textures that have not been used for at least a frame
            for (var i = 0; i < availableRenderTextures.Count; i++)
            {
                var renderTexture = availableRenderTextures[i];

                // This indicates it is empty
                if (renderTexture.renderTexture == null)
                    continue;

                if (renderTexture.isPersistent)
                    continue;

                // Don't free textures that were used in the last frame
                // TODO: Make this a configurable number of frames to avoid rapid re-allocations
                if (renderTexture.lastFrameUsed == FrameIndex)
                    continue;

                allRenderTextures.Remove(renderTexture.renderTexture);
                Object.DestroyImmediate(renderTexture.renderTexture);

                // Fill this with a null, unavailable RT and add the index to a list
                availableRenderTextures[i] = (null, renderTexture.lastFrameUsed, false, false);
                availableRtSlots.Enqueue(i);
            }

            if (!FrameDebugger.enabled)
                FrameIndex++;
        }

        public void SetRTHandleWrite(RTHandle handle, int passIndex)
        {
            if (handle.IsImported)
                return;

            if (!writtenRTHandles.Add(handle))
                return;

            var outputs = passRTHandleOutputs.GetOrAdd(passIndex);
            outputs.Add(handle);

            // Also set this as read.. incase the texture never gets used, this will ensure it at least doesn't cause leaks
            // TODO: Better approach would be to not render passes whose outputs don't get used.. though I guess its possible that some outputs will get used, but not others
            SetLastRTHandleRead(handle, passIndex);
        }

        public void SetLastRTHandleRead(RTHandle handle, int passIndex)
        {
            // Persistent handles must be freed using release persistent texture
            if (handle.IsPersistent)
                return;

            lastRtHandleRead[handle] = passIndex;
        }

        public void SetResource<T>(T resource, bool isPersistent = false) where T : IRenderPassData
        {
            Assert.IsFalse(IsExecuting);
            ResourceMap.SetRenderPassData(resource, FrameIndex, isPersistent);
        }

        public bool IsRenderPassDataValid<T>() where T : IRenderPassData
        {
            return ResourceMap.IsRenderPassDataValid<T>(FrameIndex);
        }

        public T GetResource<T>() where T : IRenderPassData
        {
            Assert.IsFalse(IsExecuting);
            return ResourceMap.GetRenderPassData<T>(FrameIndex);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue)
                return;

            if (!disposing)
                Debug.LogError("Render Graph not disposed correctly");

            ResourceMap.Dispose();

            foreach (var rt in allRenderTextures)
            {
                // Since we don't remove null entries, but rather leave them as "empty", they could be null
                if (rt != null)
                    Object.DestroyImmediate(rt);
            }

            foreach (var bufferHandle in availableBufferHandles)
                bufferHandle.handle.Dispose();

            foreach (var handle in importedBuffers)
            {
                if (handle.Value != null)
                    handle.Value.Dispose();
            }

            foreach (var importedRT in importedTextures)
                Object.DestroyImmediate(importedRT.Key);

            disposedValue = true;
        }

        public BufferHandle SetConstantBuffer<T>(in T data) where T : struct
        {
            var buffer = GetBuffer(1, UnsafeUtility.SizeOf<T>(), GraphicsBuffer.Target.Constant, GraphicsBuffer.UsageFlags.LockBufferForWrite);

            using (var pass = AddRenderPass<GlobalRenderPass>("Set Constant Buffer"))
            {
                pass.SetRenderFunction((data, buffer), (command, pass, data) =>
                {
                    var bufferData = data.buffer.Buffer.LockBufferForWrite<T>(0, 1);
                    bufferData[0] = data.data;
                    data.buffer.Buffer.UnlockBufferAfterWrite<T>(1);
                });
            }

            return buffer;
        }

        ~RenderGraph()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
