using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using static Math;

public abstract class GraphicsRenderPass<T>: RenderPass<T>
{
	private readonly List<(ResourceHandle<RenderTexture>, RenderBufferLoadAction, RenderBufferStoreAction)> colorTargets = new();
	private (ResourceHandle<RenderTexture>, RenderBufferLoadAction, RenderBufferStoreAction) depthBuffer = (new ResourceHandle<RenderTexture>(-1), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);

	private SubPassFlags flags;
	private Vector2Int? resolution;
	private bool isScreenPass;

	public int DepthSlice { get; set; } = -1;
	public int MipLevel { get; set; }
	public CubemapFace CubemapFace { get; set; } = CubemapFace.Unknown;

    public override bool IsNativeRenderPass => true;

	public override void Reset()
	{
		base.Reset();
		colorTargets.Clear();
		depthBuffer = (new ResourceHandle<RenderTexture>(-1), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
		flags = default;
		resolution = null;
		isScreenPass = default;
		DepthSlice = -1;
		MipLevel = 0;
		CubemapFace = CubemapFace.Unknown;
	}

	public void WriteTexture(ResourceHandle<RenderTexture> rtHandle, RenderBufferLoadAction loadAction = RenderBufferLoadAction.Load, RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store)
	{
		colorTargets.Add((rtHandle, loadAction, storeAction));
		WriteResource(rtHandle);
	}

	public void WriteDepth(ResourceHandle<RenderTexture> rtHandle, SubPassFlags renderTargetFlags = SubPassFlags.None, RenderBufferLoadAction loadAction = RenderBufferLoadAction.Load, RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store)
	{
		this.flags = renderTargetFlags;
		depthBuffer = (rtHandle, loadAction, storeAction);
		WriteResource(rtHandle);

		// Since depth textures are 'read' during rendering for comparisons, we also mark it as read if it's depth or stencil can be modified
		if (renderTargetFlags != SubPassFlags.ReadOnlyDepthStencil)
			RenderGraph.RtHandleSystem.ReadResource(rtHandle, Index);
	}

	private void WriteResource(ResourceHandle<RenderTexture> rtHandle)
	{
		//RenderGraph.WriteTexture(rtHandle);

		RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);

		// Check that multiple targets have the same resolution
		var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(rtHandle);
		if (!resolution.HasValue)
		{
			resolution = new Vector2Int(descriptor.width, descriptor.height);
			isScreenPass = descriptor.isScreenTexture;
		}
		else
		{
			if (resolution != new Vector2Int(descriptor.width, descriptor.height))
				throw new InvalidOperationException($"Render Pass {Name} is attempting to write to multiple textures with different resolutions");

			if (isScreenPass && !descriptor.isScreenTexture)
				throw new InvalidOperationException($"Render Pass {Name} is setting multiple targets in pass that are not marked as screen textures");

			if (!isScreenPass && !descriptor.isExactSize)
				throw new InvalidOperationException($"Render Pass {Name} is setting multiple targets in pass that are not marked as exact size textures");
		}
	}

    public override void SetupRenderPassData()
    {
        var targetCount = Max(1, colorTargets.Count);
        using var targetsScope = ArrayPool<RenderTargetIdentifier>.Get(targetCount, out var targets);

        var hasDepthBuffer = depthBuffer.Item1.Index != -1;
        Assert.IsTrue(hasDepthBuffer || targetCount > 0);

        var actualDepthTexture = GetRenderTexture(hasDepthBuffer ? depthBuffer.Item1 : colorTargets[0].Item1);
        var depthTarget = new RenderTargetIdentifier(actualDepthTexture, MipLevel, CubemapFace, DepthSlice);
        var depthLoadAction = hasDepthBuffer ? depthBuffer.Item2 : RenderBufferLoadAction.DontCare;
        var depthStoreAction = hasDepthBuffer ? depthBuffer.Item3 : RenderBufferStoreAction.DontCare;

        var clearDepth = 1f;
        var clearStencil = 0u;

        var attachmentCount = colorTargets.Count;
        if (hasDepthBuffer)
            attachmentCount++;

        var actualResolution = new Int2(0, 0);
        if (hasDepthBuffer)
        {
            var depthDesc = RenderGraph.RtHandleSystem.GetDescriptor(depthBuffer.Item1);
            if (depthDesc.clearFlags != RTClearFlags.None)
            {
                if (depthDesc.clearFlags.HasFlag(RTClearFlags.Depth))
                {
                    clearDepth = depthDesc.clearDepth;
                    depthLoadAction = RenderBufferLoadAction.Clear;
                }

                if (depthDesc.clearFlags.HasFlag(RTClearFlags.Stencil))
                {
                    clearStencil = depthDesc.clearStencil;
                    depthLoadAction = RenderBufferLoadAction.Clear;
                }

                depthDesc.clearFlags = RTClearFlags.None;

                // Now that the texture is cleared, the desc no longer needs clearing
                RenderGraph.RtHandleSystem.SetDescriptor(depthBuffer.Item1, depthDesc);
            }

            actualResolution = new(actualDepthTexture.width, actualDepthTexture.height);
            nativeRenderPassData.SetDepthTarget(depthDesc.format, depthLoadAction, depthStoreAction, depthTarget);
        }

        if (colorTargets.Count == 0)
        {
            targets[0] = depthTarget;
        }
        else
        {
            for (var i = 0; i < colorTargets.Count; i++)
            {
                var item = colorTargets[i];
                var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(item.Item1);
                Assert.AreEqual(new Vector2Int(descriptor.width, descriptor.height), resolution.Value);

                var actualTarget = GetRenderTexture(item.Item1);
                var target = new RenderTargetIdentifier(actualTarget, MipLevel, CubemapFace, DepthSlice);
                var loadAction = item.Item2;

                targets[i] = target;

                if (descriptor.clearFlags.HasFlag(RTClearFlags.Color))
                {
                    descriptor.clearFlags = RTClearFlags.None;

                    // Now that the texture is cleared, the desc no longer needs clearing
                    RenderGraph.RtHandleSystem.SetDescriptor(item.Item1, descriptor);

                    loadAction = RenderBufferLoadAction.Clear;
                }

                var index = i;
                if (hasDepthBuffer)
                    index++;
                else
                    actualResolution = new(actualTarget.width, actualTarget.height);

                nativeRenderPassData.AddAttachment(descriptor.format, loadAction, item.Item3, target, descriptor.clearColor);
            }
        }

        nativeRenderPassData.SetSize(new(actualResolution.x, actualResolution.y, 1));
        nativeRenderPassData.SetSubPassFlags(flags);
    }

    protected override void SetupTargets()
	{
        Command.SetViewport(new Rect(0, 0, resolution.Value.x >> MipLevel, resolution.Value.y >> MipLevel));
	}

	protected sealed override void PostExecute()
	{
        foreach (var colorTarget in colorTargets)
		{
			var handle = colorTarget.Item1;
			var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
			if (descriptor.autoGenerateMips)
			{
				Assert.IsTrue(descriptor.hasMips, "Trying to Generate Mips for a Texture without mips enabled");
				Command.GenerateMips(GetRenderTexture(handle));
			}
		}
	}
}