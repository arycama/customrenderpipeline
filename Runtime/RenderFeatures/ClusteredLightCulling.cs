using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ClusteredLightCulling : CameraRenderFeature
{
	[Serializable]
	public class Settings
	{
		[SerializeField] private int tileSize = 16;
		[SerializeField] private int clusterDepth = 32;
		[SerializeField] private int maxLightsPerTile = 32;

		public int TileSize => tileSize;
		public int ClusterDepth => clusterDepth;
		public int MaxLightsPerTile => maxLightsPerTile;
	}

	private readonly Settings settings;

	private static readonly uint[] zeroArray = new uint[1] { 0 };

	public ClusteredLightCulling(Settings settings, RenderGraph renderGraph) : base(renderGraph)
	{
		this.settings = settings;
	}

	public readonly struct Result : IRenderPassData
	{
		private readonly ResourceHandle<RenderTexture> lightClusterIndices;
		private readonly ResourceHandle<GraphicsBuffer> lightList;
		private readonly float clusterScale, clusterBias;
		private readonly int tileSize;

		public Result(ResourceHandle<RenderTexture> lightClusterIndices, ResourceHandle<GraphicsBuffer> lightList, float clusterScale, float clusterBias, int tileSize)
		{
			this.lightClusterIndices = lightClusterIndices;
			this.lightList = lightList;
			this.clusterScale = clusterScale;
			this.clusterBias = clusterBias;
			this.tileSize = tileSize;
		}

		public void SetInputs(RenderPass pass)
		{
			pass.ReadTexture("LightClusterIndices", lightClusterIndices);
			pass.ReadBuffer("LightClusterList", lightList);
		}

		public void SetProperties(RenderPass pass, CommandBuffer command)
		{
			pass.SetFloat("ClusterScale", clusterScale);
			pass.SetFloat("ClusterBias", clusterBias);
			pass.SetInt("TileSize", tileSize);
		}
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var clusterWidth = Math.DivRoundUp(camera.scaledPixelWidth, settings.TileSize);
		var clusterHeight = Math.DivRoundUp(camera.scaledPixelHeight, settings.TileSize);
		var clusterCount = clusterWidth * clusterHeight * settings.ClusterDepth;

		var clusterScale = settings.ClusterDepth / Mathf.Log(camera.farClipPlane / camera.nearClipPlane, 2f);
		var clusterBias = -(settings.ClusterDepth * Mathf.Log(camera.nearClipPlane, 2f) / Mathf.Log(camera.farClipPlane / camera.nearClipPlane, 2f));

		var computeShader = Resources.Load<ComputeShader>("ClusteredLightCulling");
		var lightClusterIndices = renderGraph.GetTexture(clusterWidth, clusterHeight, GraphicsFormat.R32G32_SInt, settings.ClusterDepth, TextureDimension.Tex3D);

		var lightList = renderGraph.GetBuffer(clusterCount * settings.MaxLightsPerTile);
		var counterBuffer = renderGraph.GetBuffer();

		using (var pass = renderGraph.AddComputeRenderPass("Clustered Light Culling", (tileSize: settings.TileSize, rcpClusterDepth: 1.0f / settings.ClusterDepth, counterBuffer)))
		{
			pass.Initialize(computeShader, 0, clusterWidth, clusterHeight, settings.ClusterDepth);
			pass.ReadResource<LightingSetup.Result>();

			pass.WriteBuffer("LightClusterListWrite", lightList);
			pass.WriteBuffer("LightCounter", counterBuffer);
			pass.ReadBuffer("LightCounter", counterBuffer);
			pass.WriteTexture("LightClusterIndicesWrite", lightClusterIndices);
			pass.ReadResource<ViewData>();
			pass.ReadRtHandle<HiZMaxDepth>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.SetBufferData(pass.GetBuffer(data.counterBuffer), zeroArray);
				pass.SetInt("TileSize", data.tileSize);
				pass.SetFloat("RcpClusterDepth", data.rcpClusterDepth);
			});
		}

		renderGraph.SetResource(new Result(lightClusterIndices, lightList, clusterScale, clusterBias, settings.TileSize)); ;
	}
}