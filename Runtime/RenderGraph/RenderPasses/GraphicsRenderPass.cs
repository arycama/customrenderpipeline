using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public abstract class GraphicsRenderPass<T>: RenderPass<T>
{
    public override bool IsNativeRenderPass => true;

	public override void Reset()
	{
		base.Reset();
		flags = default;
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

        // These are if statements/exceptions instead of asserts since they use the pass name to avoid constructing strings every frame and allocating gc
		if (Size != new Int2(descriptor.width, descriptor.height))
			throw new InvalidOperationException($"{Name} is attempting to write to a texture whose resolution does not match the pass");
	}

    protected override void SetupTargets()
	{
        var viewportSize = new Int2(Size.x >> MipLevel, Size.y >> MipLevel);
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