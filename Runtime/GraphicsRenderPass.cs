using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class GraphicsRenderPass : RenderPass
    {
        private readonly List<(ResourceHandle<RenderTexture>, RenderBufferLoadAction, RenderBufferStoreAction)> colorTargets = new();
        private (ResourceHandle<RenderTexture>, RenderBufferLoadAction, RenderBufferStoreAction) depthBuffer = (new ResourceHandle<RenderTexture>(-1, false), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);

        private RTClearFlags clearFlags;
        private Color clearColor;
        private float clearDepth;
        private int clearStencil;
        private RenderTargetFlags renderTargetFlags;

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
            RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);
            colorTargets.Add((rtHandle, loadAction, storeAction));
        }

        public void WriteDepth(ResourceHandle<RenderTexture> rtHandle, RenderTargetFlags renderTargetFlags = RenderTargetFlags.None, RenderBufferLoadAction loadAction = RenderBufferLoadAction.Load, RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store)
        {
            RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);
            this.renderTargetFlags = renderTargetFlags;
            depthBuffer = (rtHandle, loadAction, storeAction);
        }
        protected override void SetupTargets()
        {
            int width = 0, height = 0, targetWidth = 0, targetHeight = 0;

            var targets = ArrayPool<RenderTargetIdentifier>.Get(colorTargets.Count);
            var loads = ArrayPool<RenderBufferLoadAction>.Get(colorTargets.Count);
            var stores = ArrayPool<RenderBufferStoreAction>.Get(colorTargets.Count);

            if (depthBuffer.Item1.Index == -1)
            {
                if (colorTargets.Count == 1)
                {
                    var handle = colorTargets[0].Item1;
                    var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
                    width = descriptor.Width;
                    height = descriptor.Height;

                    command.SetRenderTarget(GetRenderTexture(handle), MipLevel, CubemapFace.Unknown, DepthSlice);
                }
                else
                {
                    for (var i = 0; i < colorTargets.Count; i++)
                    {
                        var item = colorTargets[i];
                        var handle = item.Item1;
                        var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);

                        width = descriptor.Width;
                        height = descriptor.Height;

                        targets[i] = GetRenderTexture(handle);
                        loads[i] = item.Item2;
                        stores[i] = item.Item3;
                    }

                    command.SetRenderTarget(targets, targets[0]);
                }
            }
            else
            {
                var depthHandle = depthBuffer.Item1;
                var depthDescriptor = RenderGraph.RtHandleSystem.GetDescriptor(depthHandle);
                width = depthDescriptor.Width;
                height = depthDescriptor.Height;

                var depthTarget = GetRenderTexture(depthHandle);
                targetWidth = depthTarget.width;
                targetHeight = depthTarget.height;

                if (colorTargets.Count == 0)
                {
                    command.SetRenderTarget(depthTarget, depthBuffer.Item2, depthBuffer.Item3, depthTarget, depthBuffer.Item2, depthBuffer.Item3);
                }
                else
                {
                    for (var i = 0; i < colorTargets.Count; i++)
                    {
                        var item = colorTargets[i];
                        var handle = item.Item1;
                        var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);

                        width = descriptor.Width;
                        height = descriptor.Height;

                        targets[i] = GetRenderTexture(handle);
                        loads[i] = item.Item2;
                        stores[i] = item.Item3;
                    }

                    var binding = new RenderTargetBinding(targets, loads, stores, depthTarget, depthBuffer.Item2, depthBuffer.Item3) { flags = renderTargetFlags };
                    command.SetRenderTarget(binding);

                }
            }

            if (clearFlags != RTClearFlags.None)
                command.ClearRenderTarget(clearFlags, clearColor, clearDepth, (uint)clearStencil);

            command.SetViewport(new Rect(0, 0, width, height));

            ArrayPool<RenderTargetIdentifier>.Release(targets);
            ArrayPool<RenderBufferLoadAction>.Release(loads);
            ArrayPool<RenderBufferStoreAction>.Release(stores);
        }

        protected sealed override void PostExecute()
        {
            foreach (var colorTarget in colorTargets)
            {
                var handle = colorTarget.Item1;
                var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
                if (descriptor.AutoGenerateMips)
                    command.GenerateMips(GetRenderTexture(handle));
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