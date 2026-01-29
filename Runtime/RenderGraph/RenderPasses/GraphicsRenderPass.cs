using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public abstract class GraphicsRenderPass<T>: RenderPass<T>
{
	private Int2? resolution;
	private bool isScreenPass;

    public override bool IsNativeRenderPass => true;

	public override void Reset()
	{
		base.Reset();
		flags = default;
		resolution = null;
		isScreenPass = default;
		DepthSlice = -1;
		MipLevel = 0;
		CubemapFace = CubemapFace.Unknown;
	}

	public void WriteTexture(ResourceHandle<RenderTexture> rtHandle)
	{
		colorTargets.Add(rtHandle);
		WriteResource(rtHandle);
	}

    public void WriteDepth(ResourceHandle<RenderTexture> rtHandle, SubPassFlags flags = SubPassFlags.None)
	{
		this.flags = flags;
		depthBuffer = rtHandle;
		WriteResource(rtHandle);

		// Since depth textures are 'read' during rendering for comparisons, we also mark it as read if it's depth or stencil can be modified
		if (flags != SubPassFlags.ReadOnlyDepthStencil)
			RenderGraph.RtHandleSystem.ReadResource(rtHandle, Index);
	}

	private void WriteResource(ResourceHandle<RenderTexture> rtHandle)
	{
		RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);

		// Check that multiple targets have the same resolution
		var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(rtHandle);
		if (!resolution.HasValue)
		{
			resolution = new(descriptor.width, descriptor.height);
			isScreenPass = descriptor.isScreenTexture;
		}
		else
		{
            // These are if statements/exceptions instead of asserts since they use the pass name to avoid constructing strings every frame and allocating gc
			if (resolution.Value != new Int2(descriptor.width, descriptor.height))
				throw new InvalidOperationException($"Render Pass {Name} is attempting to write to multiple textures with different resolutions");

			if (isScreenPass && !descriptor.isScreenTexture)
				throw new InvalidOperationException($"Render Pass {Name} is setting multiple targets in pass that are not marked as screen textures");

			if (!isScreenPass && !descriptor.isExactSize)
				throw new InvalidOperationException($"Render Pass {Name} is setting multiple targets in pass that are not marked as exact size textures");
		}
	}

    protected override void SetupTargets()
	{
        var viewportSize = new Int2(resolution.Value.x >> MipLevel, resolution.Value.y >> MipLevel);
        if(viewportSize != size.xy)
            Command.SetViewport(new Rect(0, 0, viewportSize.x, viewportSize.y));
	}

	protected sealed override void PostExecute()
	{
        foreach (var colorTarget in colorTargets)
		{
            var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(colorTarget);
			if (descriptor.autoGenerateMips)
			{
				Assert.IsTrue(descriptor.hasMips, "Trying to Generate Mips for a Texture without mips enabled");
				Command.GenerateMips(GetRenderTexture(colorTarget));
			}
		}
	}
}