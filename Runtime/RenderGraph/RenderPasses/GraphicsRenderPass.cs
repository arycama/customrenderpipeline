using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using static Math;

public abstract class GraphicsRenderPass : RenderPass
{
	private readonly List<(ResourceHandle<RenderTexture>, RenderBufferLoadAction, RenderBufferStoreAction)> colorTargets = new();
	private (ResourceHandle<RenderTexture>, RenderBufferLoadAction, RenderBufferStoreAction) depthBuffer = (new ResourceHandle<RenderTexture>(-1), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);

	private RenderTargetFlags renderTargetFlags;
	private Vector2Int? resolution;
	private bool isScreenPass;

	public int DepthSlice { get; set; } = -1;
	public int MipLevel { get; set; }
	public CubemapFace CubemapFace { get; set; } = CubemapFace.Unknown;

	public override void Reset()
	{
		base.Reset();
		colorTargets.Clear();
		depthBuffer = (new ResourceHandle<RenderTexture>(-1), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
		renderTargetFlags = default;
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

	public void WriteDepth(ResourceHandle<RenderTexture> rtHandle, RenderTargetFlags renderTargetFlags = RenderTargetFlags.None, RenderBufferLoadAction loadAction = RenderBufferLoadAction.Load, RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store)
	{
		this.renderTargetFlags = renderTargetFlags;
		depthBuffer = (rtHandle, loadAction, storeAction);
		WriteResource(rtHandle);

		// Since depth textures are 'read' during rendering for comparisons, we also mark it as read if it's depth or stencil can be modified
		if (renderTargetFlags != RenderTargetFlags.ReadOnlyDepthStencil)
			RenderGraph.RtHandleSystem.ReadResource(rtHandle, Index);
	}

	private void WriteResource(ResourceHandle<RenderTexture> rtHandle)
	{
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

	protected override void SetupTargets()
	{
		var targetCount = Max(1, colorTargets.Count);
		using var targetsScope = ArrayPool<RenderTargetIdentifier>.Get(targetCount, out var targets);
		using var loadsScope = ArrayPool<RenderBufferLoadAction>.Get(targetCount, out var loads);
		using var storesScope = ArrayPool<RenderBufferStoreAction>.Get(targetCount, out var stores);
		using var clearColorsScope = ArrayPool<Color>.Get(targetCount, out var clearColors);

		var hasDepthBuffer = depthBuffer.Item1.Index != -1;
		Assert.IsTrue(hasDepthBuffer || targetCount > 0);

		var depthTarget = new RenderTargetIdentifier(GetRenderTexture(hasDepthBuffer ? depthBuffer.Item1 : colorTargets[0].Item1), MipLevel, CubemapFace, DepthSlice);
		var depthLoadAction = hasDepthBuffer ? depthBuffer.Item2 : RenderBufferLoadAction.DontCare;
		var depthStoreAction = hasDepthBuffer ? depthBuffer.Item3 : RenderBufferStoreAction.DontCare;

		var clearFlags = RTClearFlags.None;
		var clearDepth = 1f;
		var clearStencil = 0u;

		if (hasDepthBuffer)
		{
			var depthDesc = RenderGraph.RtHandleSystem.GetDescriptor(depthBuffer.Item1);
			if (depthDesc.clearFlags != RTClearFlags.None)
			{
				if (depthDesc.clearFlags.HasFlag(RTClearFlags.Depth))
				{
					clearFlags |= RTClearFlags.Depth;
					clearDepth = depthDesc.clearDepth;
				}

				if (depthDesc.clearFlags.HasFlag(RTClearFlags.Stencil))
				{
					clearFlags |= RTClearFlags.Stencil;
					clearStencil = depthDesc.clearStencil;
				}

				depthDesc.clearFlags = RTClearFlags.None;

				// Now that the texture is cleared, the desc no longer needs clearing
				RenderGraph.RtHandleSystem.SetDescriptor(depthBuffer.Item1, depthDesc);
			}
		}

		if (colorTargets.Count == 0)
		{
			targets[0] = depthTarget;
			loads[0] = depthLoadAction;
			stores[0] = depthStoreAction;
		}
		else
		{
			for (var i = 0; i < colorTargets.Count; i++)
			{
				var item = colorTargets[i];
				var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(item.Item1);
				Assert.AreEqual(new Vector2Int(descriptor.width, descriptor.height), resolution.Value);

				targets[i] = new RenderTargetIdentifier(GetRenderTexture(item.Item1), MipLevel, CubemapFace, DepthSlice);
				loads[i] = item.Item2;
				stores[i] = item.Item3;

				if (descriptor.clearFlags.HasFlag(RTClearFlags.Color))
				{
					clearColors[i] = descriptor.clearColor;

					switch(i)
					{
						case 0:
							clearFlags |= RTClearFlags.Color0;
							break;
						case 1:
							clearFlags |= RTClearFlags.Color1;
							break;
						case 2:
							clearFlags |= RTClearFlags.Color2;
							break;
						case 3:
							clearFlags |= RTClearFlags.Color3;
							break;
						case 4:
							clearFlags |= RTClearFlags.Color4;
							break;
						case 5:
							clearFlags |= RTClearFlags.Color5;
							break;
						case 6:
							clearFlags |= RTClearFlags.Color6;
							break;
						case 7:
							clearFlags |= RTClearFlags.Color7;
							break;
					}

					descriptor.clearFlags = RTClearFlags.None;

					// Now that the texture is cleared, the desc no longer needs clearing
					RenderGraph.RtHandleSystem.SetDescriptor(item.Item1, descriptor);
				}
			}
		}

		var binding = new RenderTargetBinding(targets, loads, stores, depthTarget, depthLoadAction, depthStoreAction) { flags = renderTargetFlags };
		Command.SetRenderTarget(binding);

		if (clearFlags != RTClearFlags.None)
			Command.ClearRenderTarget(clearFlags, clearColors, clearDepth, clearStencil);

		if (!resolution.HasValue)
			throw new InvalidOperationException($"Render Pass {Name} has no resolution set, does it write to any textures");

		Command.SetViewport(new Rect(0, 0, resolution.Value.x >> MipLevel, resolution.Value.y >> MipLevel));
	}

	protected sealed override void PostExecute()
	{
		Command.ClearRandomWriteTargets();

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

		// Reset all properties
		depthBuffer = default;
		colorTargets.Clear();
		renderTargetFlags = RenderTargetFlags.None;
		DepthSlice = -1;
		MipLevel = 0;
	}
}