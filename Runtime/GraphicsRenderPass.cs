using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class GraphicsRenderPass : RenderPass
    {
        private readonly List<(RTHandle, RenderBufferLoadAction, RenderBufferStoreAction)> colorTargets = new();
        private (RTHandle, RenderBufferLoadAction, RenderBufferStoreAction) depthBuffer;

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

        public void WriteTexture(RTHandle rtHandle, RenderBufferLoadAction loadAction = RenderBufferLoadAction.Load, RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store)
        {
            RenderGraph.RtHandleSystem.WriteTexture(rtHandle, Index);
            colorTargets.Add((rtHandle, loadAction, storeAction));
        }

        public void WriteDepth(RTHandle rtHandle, RenderTargetFlags renderTargetFlags = RenderTargetFlags.None, RenderBufferLoadAction loadAction = RenderBufferLoadAction.Load, RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store)
        {
            RenderGraph.RtHandleSystem.WriteTexture(rtHandle, Index);
            this.renderTargetFlags = renderTargetFlags;
            depthBuffer = (rtHandle, loadAction, storeAction);
        }
        protected override void SetupTargets()
        {
            int width = 0, height = 0, targetWidth = 0, targetHeight = 0;

            var targets = ArrayPool<RenderTargetIdentifier>.Get(colorTargets.Count);
            var loads = ArrayPool<RenderBufferLoadAction>.Get(colorTargets.Count);
            var stores = ArrayPool<RenderBufferStoreAction>.Get(colorTargets.Count);

            if (depthBuffer.Item1 == null)
            {
                if (colorTargets.Count == 1)
                {
                    width = colorTargets[0].Item1.Width;
                    height = colorTargets[0].Item1.Height;

                    command.SetRenderTarget(colorTargets[0].Item1, MipLevel, CubemapFace.Unknown, DepthSlice);
                }
                else
                {
                    for (var i = 0; i < colorTargets.Count; i++)
                    {
                        Assert.IsTrue(targetWidth == 0 || targetWidth == colorTargets[i].Item1.RenderTexture.width, Name);
                        Assert.IsTrue(targetHeight == 0 || targetHeight == colorTargets[i].Item1.RenderTexture.height, Name);

                        width = colorTargets[i].Item1.Width;
                        height = colorTargets[i].Item1.Height;

                        targets[i] = colorTargets[i].Item1;
                        loads[i] = colorTargets[i].Item2;
                        stores[i] = colorTargets[i].Item3;
                    }

                    command.SetRenderTarget(targets, targets[0]);
                }
            }
            else
            {
                width = depthBuffer.Item1.Width;
                height = depthBuffer.Item1.Height;
                targetWidth = depthBuffer.Item1.RenderTexture.width;
                targetHeight = depthBuffer.Item1.RenderTexture.height;

                if (colorTargets.Count == 0)
                {
                    command.SetRenderTarget(depthBuffer.Item1, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare, depthBuffer.Item1, depthBuffer.Item2, depthBuffer.Item3);
                }
                else
                {
                    for (var i = 0; i < colorTargets.Count; i++)
                    {
                        Assert.IsTrue(targetWidth == 0 || targetWidth == colorTargets[i].Item1.RenderTexture.width, Name);
                        Assert.IsTrue(targetHeight == 0 || targetHeight == colorTargets[i].Item1.RenderTexture.height, Name);

                        width = colorTargets[i].Item1.Width;
                        height = colorTargets[i].Item1.Height;

                        targets[i] = colorTargets[i].Item1;
                        loads[i] = colorTargets[i].Item2;
                        stores[i] = colorTargets[i].Item3;
                    }

                    var binding = new RenderTargetBinding(targets, loads, stores, depthBuffer.Item1, depthBuffer.Item2, depthBuffer.Item3) { flags = renderTargetFlags };
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
                if (colorTarget.Item1.AutoGenerateMips)
                    command.GenerateMips(colorTarget.Item1);
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