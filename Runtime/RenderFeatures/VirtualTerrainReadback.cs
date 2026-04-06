using System;
using UnityEngine;
using UnityEngine.Rendering;

public class VirtualTerrainReadback : ViewRenderFeature
{
	private readonly ComputeShader virtualTextureUpdateShader;
    private readonly TerrainSettings settings;
    private readonly Action<AsyncGPUReadbackRequest> OnReadbackComplete;

	private int PageTableSize => settings.VirtualResolution / settings.TileResolution;

    public VirtualTerrainReadback(RenderGraph renderGraph, TerrainSettings settings, VirtualTerrain virtualTerrain) : base(renderGraph)
	{
		this.settings = settings;
		virtualTextureUpdateShader = Resources.Load<ComputeShader>("Terrain/VirtualTextureUpdate");
        OnReadbackComplete = virtualTerrain.ReadbackRequestComplete;
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		var maxRequestBufferSize = viewRenderData.viewSize.x * viewRenderData.viewSize.y + 1;
        var requestBuffer = renderGraph.GetBuffer(maxRequestBufferSize, target: GraphicsBuffer.Target.Append);
        var virtualTextureData = renderGraph.GetResource<VirtualTextureData>();
        using (var pass = renderGraph.AddComputeRenderPass("Gather Requested Pages", (requestBuffer, virtualTextureUpdateShader, PageTableSize)))
		{
			var threadCount = Texture2DExtensions.PixelCount(PageTableSize);
			pass.Initialize(virtualTextureUpdateShader, 5, threadCount);
            pass.WriteBuffer("VirtualRequests", requestBuffer);
            pass.WriteBuffer("VirtualFeedbackTexture", virtualTextureData.feedbackBuffer);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                command.ClearRandomWriteTargets(); // Clear from previous passes
                command.SetBufferCounterValue(pass.GetBuffer(data.requestBuffer), 1); // Init the counter to zero before appending
                pass.SetInt("PageTableSize", data.PageTableSize);
            });
		}

        using (var pass = renderGraph.AddGenericRenderPass("Copy Counter and readback", (requestBuffer, OnReadbackComplete)))
		{
            pass.ReadBuffer("", requestBuffer);
			pass.SetRenderFunction(static (command, pass, data) =>
			{
                command.CopyCounterValue(pass.GetBuffer(data.requestBuffer), pass.GetBuffer(data.requestBuffer), 0);
                command.RequestAsyncReadback(pass.GetBuffer(data.requestBuffer), data.OnReadbackComplete);
            });
		}
	}
}
