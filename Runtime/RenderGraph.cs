using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.AI;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arycama.CustomRenderPipeline
{
    public class RenderGraph : IDisposable
    {
        private readonly RTHandleSystem rtHandleSystem;
        private readonly BufferHandleSystem bufferHandleSystem;

        private readonly List<RenderPass> renderPasses = new();

        private readonly Dictionary<Type, Queue<RenderPass>> renderPassPool = new();
        private readonly Dictionary<Type, Queue<RenderGraphBuilder>> builderPool = new();
        private readonly Dictionary<int, List<RTHandle>> lastPassOutputs = new();

        public bool IsExecuting { get; private set; }

        private readonly Dictionary<RTHandle, int> lastRtHandleRead = new();
        private readonly Dictionary<int, List<RTHandle>> passRTHandleOutputs = new();
        private readonly HashSet<RTHandle> writtenRTHandles = new();

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

        private int screenWidth, screenHeight;
        private bool disposedValue;

        public RenderGraph(CustomRenderPipeline renderPipeline)
        {
            rtHandleSystem = new();
            bufferHandleSystem = new();

            EmptyBuffer = bufferHandleSystem.ImportBuffer(new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int)) { name = "Empty Structured Buffer" });
            EmptyTexture = rtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { hideFlags = HideFlags.HideAndDontSave });
            EmptyUavTexture = rtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = true });
            EmptyTextureArray = rtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex2DArray, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave });
            Empty3DTexture = rtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex3D, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave });
            EmptyCubemap = rtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Cube, hideFlags = HideFlags.HideAndDontSave });
            EmptyCubemapArray = rtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.CubeArray, volumeDepth = 6, hideFlags = HideFlags.HideAndDontSave });

            ResourceMap = new(this);
            RenderPipeline = renderPipeline;
        }

        public BufferHandle ImportBuffer(GraphicsBuffer buffer)
        {
            return bufferHandleSystem.ImportBuffer(buffer);
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
            bufferHandleSystem.CreateBuffers();
            
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

                        handle.RenderTexture = rtHandleSystem.GetTexture(handle, FrameIndex, screenWidth, screenHeight);
                    }
                }

                // Release any textures if this was their final read
                if (!lastPassOutputs.TryGetValue(i, out var outputsToFree))
                    continue;

                foreach (var output in outputsToFree)
                {
                    if (output.IsImported)
                        continue;

                    rtHandleSystem.MakeTextureAvailable(output, FrameIndex);
                }
            }

            IsExecuting = true;

            foreach (var renderPass in renderPasses)
                renderPass.Run(command);

            IsExecuting = false;
        }

        public RTHandle GetTexture(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false, bool isExactSize = false)
        {
            Assert.IsFalse(IsExecuting);
            return rtHandleSystem.GetTexture(width, height, format, volumeDepth, dimension, isScreenTexture, hasMips, autoGenerateMips, isPersistent, isExactSize);
        }

        public BufferHandle GetBuffer(int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
        {
            // Ensure we're not getting a texture during execution, this must be done in the setup
            Assert.IsFalse(IsExecuting);

            return bufferHandleSystem.GetBuffer(FrameIndex, count, stride, target, usageFlags);
        }

        public void CleanupCurrentFrame()
        {
            // Release all pooled passes
            foreach (var pass in renderPasses)
                renderPassPool[pass.GetType()].Enqueue(pass);

            renderPasses.Clear();
            lastRtHandleRead.Clear();
            writtenRTHandles.Clear();

            foreach (var list in passRTHandleOutputs)
                list.Value.Clear();

            foreach (var output in lastPassOutputs)
                output.Value.Clear();

            bufferHandleSystem.CleanupCurrentFrame(FrameIndex);

            rtHandleSystem.FreeThisFramesTextures(FrameIndex);

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
            rtHandleSystem.Dispose();
            bufferHandleSystem.Dispose();
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
