﻿using System;
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
        private bool disposedValue;
        private readonly List<RenderPass> renderPasses = new();

        public RTHandleSystem RtHandleSystem { get; }
        public BufferHandleSystem BufferHandleSystem { get; }
        public RenderResourceMap ResourceMap { get; }
        public CustomRenderPipeline RenderPipeline { get; }

        public BufferHandle EmptyBuffer { get; }
        public RTHandle EmptyTexture { get; }
        public RTHandle EmptyUavTexture { get; }
        public RTHandle EmptyTextureArray { get; }
        public RTHandle Empty3DTexture { get; }
        public RTHandle EmptyCubemap { get; }
        public RTHandle EmptyCubemapArray { get; }

        public int FrameIndex { get; private set; }
        public bool IsExecuting { get; private set; }

        public RenderGraph(CustomRenderPipeline renderPipeline)
        {
            RtHandleSystem = new(this);
            BufferHandleSystem = new(this);

            EmptyBuffer = BufferHandleSystem.ImportBuffer(new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int)) { name = "Empty Structured Buffer" });
            EmptyTexture = RtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { hideFlags = HideFlags.HideAndDontSave });
            EmptyUavTexture = RtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = true });
            EmptyTextureArray = RtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex2DArray, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave });
            Empty3DTexture = RtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex3D, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave });
            EmptyCubemap = RtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.Cube, hideFlags = HideFlags.HideAndDontSave });
            EmptyCubemapArray = RtHandleSystem.ImportRenderTexture(new RenderTexture(1, 1, 0) { dimension = TextureDimension.CubeArray, volumeDepth = 6, hideFlags = HideFlags.HideAndDontSave });

            ResourceMap = new(this);
            RenderPipeline = renderPipeline;
        }

        ~RenderGraph()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue)
                return;

            if (!disposing)
                Debug.LogError("Render Graph not disposed correctly");

            EmptyBuffer.Dispose();
            Object.DestroyImmediate(EmptyTexture.RenderTexture);
            Object.DestroyImmediate(EmptyUavTexture.RenderTexture);
            Object.DestroyImmediate(EmptyTextureArray.RenderTexture);
            Object.DestroyImmediate(Empty3DTexture.RenderTexture);
            Object.DestroyImmediate(EmptyCubemap.RenderTexture);
            Object.DestroyImmediate(EmptyCubemapArray.RenderTexture);

            ResourceMap.Dispose();
            RtHandleSystem.Dispose();
            BufferHandleSystem.Dispose();
            disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public T AddRenderPass<T>(string name) where T : RenderPass, new()
        {
            var result = new T
            {
                RenderGraph = this,
                Name = name,
                Index = renderPasses.Count
            };

            renderPasses.Add(result);
            return result;
        }

        public void Execute(CommandBuffer command)
        {
            BufferHandleSystem.CreateBuffers();

            RtHandleSystem.AllocateFrameTextures(renderPasses.Count, FrameIndex);

            IsExecuting = true;

            foreach (var renderPass in renderPasses)
                renderPass.Run(command);

            IsExecuting = false;
        }

        public RTHandle GetTexture(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false)
        {
            return RtHandleSystem.GetTexture(width, height, format, volumeDepth, dimension, isScreenTexture, hasMips, autoGenerateMips, isPersistent);
        }

        public BufferHandle GetBuffer(int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
        {
            return BufferHandleSystem.GetBuffer(FrameIndex, count, stride, target, usageFlags);
        }

        public void WriteTexture(RTHandle handle, int passIndex)
        {
            RtHandleSystem.WriteTexture(handle, passIndex);
        }

        public void CleanupCurrentFrame()
        {
            renderPasses.Clear();

            BufferHandleSystem.CleanupCurrentFrame(FrameIndex);
            RtHandleSystem.CleanupCurrentFrame(FrameIndex);

            if (!FrameDebugger.enabled)
                FrameIndex++;
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

        public BufferHandle SetConstantBuffer<T>(in T data) where T : struct
        {
            var buffer = BufferHandleSystem.GetBuffer(FrameIndex, 1, UnsafeUtility.SizeOf<T>(), GraphicsBuffer.Target.Constant, GraphicsBuffer.UsageFlags.LockBufferForWrite);
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
    }
}
