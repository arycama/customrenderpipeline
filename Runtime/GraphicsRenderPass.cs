using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class GraphicsRenderPass : RenderPass
    {
        private readonly List<(ResourceHandle<RenderTexture>, RenderBufferLoadAction, RenderBufferStoreAction)> colorTargets = new();
        private (ResourceHandle<RenderTexture>, RenderBufferLoadAction, RenderBufferStoreAction) depthBuffer = (new ResourceHandle<RenderTexture>(-1), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);

        private RTClearFlags clearFlags;
        private Color clearColor;
        private float clearDepth;
        private int clearStencil;
        private RenderTargetFlags renderTargetFlags;
        private Vector2Int? resolution;
        private bool isScreenPass;

        public int DepthSlice { get; set; } = -1;
        public int MipLevel { get; set; }

        public void ConfigureClear(RTClearFlags clearFlags, Color clearColor = default, float clearDepth = 1.0f, int clearStencil = 0)
        {
            this.clearFlags = clearFlags;
            this.clearColor = clearColor;
            this.clearDepth = clearDepth;
            this.clearStencil = clearStencil;
        }

        public void WriteTexture(ResourceHandle<RenderTexture> rtHandle, RenderBufferLoadAction loadAction = RenderBufferLoadAction.Load, RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store)
        {
            colorTargets.Add((rtHandle, loadAction, storeAction));
            WriteResource(rtHandle);
        }

        public void WriteDepth(ResourceHandle<RenderTexture> rtHandle, RenderTargetFlags renderTargetFlags = RenderTargetFlags.None, RenderBufferLoadAction loadAction = RenderBufferLoadAction.Load, RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store)
        {
            this.renderTargetFlags = renderTargetFlags;
            depthBuffer = (rtHandle, loadAction, storeAction);
            WriteResource(rtHandle);
        }

        private void WriteResource(ResourceHandle<RenderTexture> rtHandle)
        {
            RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);

            // Check that multiple targets have the same resolution
            var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(rtHandle);
            if (!resolution.HasValue)
            {
                resolution = new Vector2Int(descriptor.Width, descriptor.Height);
                isScreenPass = descriptor.IsScreenTexture;
            }
            else
            {
                if (resolution != new Vector2Int(descriptor.Width, descriptor.Height))
                    throw new InvalidOperationException($"Render Pass {Name} is attempting to write to multiple textures with different resolutions");

                if (isScreenPass && !descriptor.IsScreenTexture)
                    throw new InvalidOperationException($"Render Pass {Name} is setting multiple targets in pass that are not marked as screen textures");

                if(!isScreenPass && !descriptor.IsExactSize)
                    throw new InvalidOperationException($"Render Pass {Name} is setting multiple targets in pass that are not marked as exact size textures");
            }
        }

        protected override void SetupTargets()
        {
            var targets = ArrayPool<RenderTargetIdentifier>.Get(colorTargets.Count);
            var loads = ArrayPool<RenderBufferLoadAction>.Get(colorTargets.Count);
            var stores = ArrayPool<RenderBufferStoreAction>.Get(colorTargets.Count);

            if (depthBuffer.Item1.Index == -1)
            {
                if (colorTargets.Count == 1)
                {
                    var handle = colorTargets[0].Item1;
                    var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
                    Assert.AreEqual(new Vector2Int(descriptor.Width, descriptor.Height), resolution.Value);
                    Command.SetRenderTarget(GetRenderTexture(handle), MipLevel, CubemapFace.Unknown, DepthSlice);
                }
                else
                {
                    for (var i = 0; i < colorTargets.Count; i++)
                    {
                        var item = colorTargets[i];
                        var handle = item.Item1;
                        var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
                        Assert.AreEqual(new Vector2Int(descriptor.Width, descriptor.Height), resolution.Value);
                        targets[i] = GetRenderTexture(handle);
                        loads[i] = item.Item2;
                        stores[i] = item.Item3;
                    }

                    Command.SetRenderTarget(targets, targets[0]);
                }
            }
            else
            {
                var depthHandle = depthBuffer.Item1;
                var depthTarget = GetRenderTexture(depthHandle);

                if (colorTargets.Count == 0)
                {
                    var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(depthHandle);
                    Assert.AreEqual(new Vector2Int(descriptor.Width, descriptor.Height), resolution.Value);

                    Command.SetRenderTarget(depthTarget, depthBuffer.Item2, depthBuffer.Item3, depthTarget, depthBuffer.Item2, depthBuffer.Item3);
                }
                else
                {
                    for (var i = 0; i < colorTargets.Count; i++)
                    {
                        var item = colorTargets[i];
                        var handle = item.Item1;
                        var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
                        Assert.AreEqual(new Vector2Int(descriptor.Width, descriptor.Height), resolution.Value);

                        targets[i] = GetRenderTexture(handle);
                        loads[i] = item.Item2;
                        stores[i] = item.Item3;
                    }

                    var binding = new RenderTargetBinding(targets, loads, stores, depthTarget, depthBuffer.Item2, depthBuffer.Item3) { flags = renderTargetFlags };
                    Command.SetRenderTarget(binding);

                }
            }

            if (clearFlags != RTClearFlags.None)
                Command.ClearRenderTarget(clearFlags, clearColor, clearDepth, (uint)clearStencil);

            if(!resolution.HasValue)
            {
                throw new InvalidOperationException($"Render Pass {Name} has no resolution set, does it write to any textures");
            }

            Command.SetViewport(new Rect(0, 0, resolution.Value.x, resolution.Value.y));

            ArrayPool<RenderTargetIdentifier>.Release(targets);
            ArrayPool<RenderBufferLoadAction>.Release(loads);
            ArrayPool<RenderBufferStoreAction>.Release(stores);
        }

        protected sealed override void PostExecute()
        {
            Command.ClearRandomWriteTargets();

            foreach (var colorTarget in colorTargets)
            {
                var handle = colorTarget.Item1;
                var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
                if (descriptor.AutoGenerateMips)
                {
                    Assert.IsTrue(descriptor.HasMips, "Trying to Generate Mips for a Texture without mips enabled");
                    Command.GenerateMips(GetRenderTexture(handle));
                }
            }

            // Reset all properties
            depthBuffer = default;
            colorTargets.Clear();
            clearFlags = RTClearFlags.None;
            clearColor = Color.clear;
            clearDepth = 1.0f;
            clearStencil = 0;
            renderTargetFlags = RenderTargetFlags.None;
            DepthSlice = -1;
            MipLevel = 0;
        }
    }
}