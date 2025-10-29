using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Math;

public partial class VolumetricLighting : CameraRenderFeature
{
    private readonly Settings settings;
    private readonly PersistentRTHandleCache colorHistory;

    public VolumetricLighting(Settings settings, RenderGraph renderGraph) : base(renderGraph)
    {
        this.settings = settings;
        colorHistory = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Volumetric Lighting", TextureDimension.Tex3D);
    }

    protected override void Cleanup(bool disposing)
    {
        colorHistory.Dispose();
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
		renderGraph.AddProfileBeginPass("Volumetric Lighting");

		var volumeWidth = DivRoundUp(camera.scaledPixelWidth, settings.TileSize);
        var volumeHeight = DivRoundUp(camera.scaledPixelHeight, settings.TileSize);
		var (current, history, wasCreated) = colorHistory.GetTextures(volumeWidth, volumeHeight, camera, settings.DepthSlices);

		var linearToVolumetricScale = Rcp(Log2(settings.MaxDistance / camera.nearClipPlane));
		var volumetricLightingData = renderGraph.SetConstantBuffer(new VolumetricLightingData
		(
			linearToVolumetricScale,
			-Log2(camera.nearClipPlane) * linearToVolumetricScale,
			(Log2(settings.MaxDistance) - Log2(camera.nearClipPlane)) / settings.DepthSlices,
			Log2(camera.nearClipPlane),
			volumeWidth,
			volumeHeight,
			settings.DepthSlices,
			Rcp(settings.DepthSlices),
			(uint)Log2(settings.TileSize),
			(float)settings.BlurSigma,
			(uint)settings.DepthSlices,
			0f
		));

		var rawJitter = renderGraph.GetResource<TemporalAASetupData>().jitter;
		var jitter = 2.0f * rawJitter / (Float2)camera.ScaledViewSize();
		var tanHalfFov = Tan(0.5f * Radians(camera.fieldOfView));
		var pixelToWorldViewDir = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(volumeWidth, volumeHeight, jitter, tanHalfFov, camera.aspect, Matrix4x4.Rotate(camera.transform.rotation));

        var computeShader = Resources.Load<ComputeShader>("VolumetricLighting");
		using (var pass = renderGraph.AddComputeRenderPass("Volumetric Lighting", (pixelToWorldViewDir, history)))
        {
            pass.Initialize(computeShader, 0, volumeWidth, volumeHeight, settings.DepthSlices);
            pass.WriteTexture("Result", current);

            pass.ReadTexture("Input", history);
			pass.ReadBuffer("VolumetricLightingData", volumetricLightingData);

            pass.ReadResource<ClusteredLightCulling.Result>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<LightingSetup.Result>();
            pass.ReadResource<ShadowData>();
            pass.ReadResource<CloudShadowDataResult>();
            pass.ReadResource<ViewData>();
            pass.ReadRtHandle<HiZMaxDepth>();
            pass.ReadResource<ParticleShadowData>();

			pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetMatrix("PixelToWorldViewDir", data.pixelToWorldViewDir);
				pass.SetVector("InputScale", pass.RenderGraph.GetScale3D(data.history));
                pass.SetVector("InputMax", pass.RenderGraph.GetLimit3D(data.history));
            });
        }

		// Filter X
		var finalInput = current;
		if (settings.BlurSigma > 0)
		{
			var filterX = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, settings.DepthSlices, TextureDimension.Tex3D);
			using (var pass = renderGraph.AddComputeRenderPass("Filter X"))
			{
				pass.Initialize(computeShader, 1, volumeWidth, volumeHeight, settings.DepthSlices);
				pass.WriteTexture("Result", filterX);
				pass.ReadBuffer("VolumetricLightingData", volumetricLightingData);
				pass.ReadTexture("Input", current);
				pass.ReadRtHandle<HiZMaxDepth>();
			}

			// Filter Y
			var filterY = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, settings.DepthSlices, TextureDimension.Tex3D);
			using (var pass = renderGraph.AddComputeRenderPass("Filter Y"))
			{
				pass.Initialize(computeShader, 2, volumeWidth, volumeHeight, settings.DepthSlices);
				pass.WriteTexture("Result", filterY);
				pass.ReadBuffer("VolumetricLightingData", volumetricLightingData);
				pass.ReadTexture("Input", filterX);
				pass.ReadRtHandle<HiZMaxDepth>();
			}

			finalInput = filterY;
		}

		// Accumulate
		var volumetricLight = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, settings.DepthSlices, TextureDimension.Tex3D);
		using (var pass = renderGraph.AddComputeRenderPass("Accumulate", pixelToWorldViewDir))
        {
            pass.Initialize(computeShader, 3, volumeWidth, volumeHeight, 1);
            pass.WriteTexture("Result", volumetricLight);
			pass.ReadBuffer("VolumetricLightingData", volumetricLightingData);
			pass.ReadTexture("Input", finalInput);
            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<ViewData>();
			pass.ReadRtHandle<HiZMaxDepth>();

			pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetMatrix("PixelToWorldViewDir", data);
            });
        }

		var result = new Result(volumetricLight, volumetricLightingData);
		renderGraph.SetResource(result);

		renderGraph.AddProfileEndPass("Volumetric Lighting");
	}
}

internal struct VolumetricLightingData
{
	public float linearToVolumetricScale;
	public float Item2;
	public float Item3;
	public float Item4;
	public float Item5;
	public float Item6;
	public float Item7;
	public float Item8;
	public uint Item9;
	public float Item10;
	public uint Item11;
	public float Item12;

	public VolumetricLightingData(float linearToVolumetricScale, float item2, float item3, float item4, float item5, float item6, float item7, float item8, uint item9, float item10, uint item11, float item12)
	{
		this.linearToVolumetricScale = linearToVolumetricScale;
		Item2 = item2;
		Item3 = item3;
		Item4 = item4;
		Item5 = item5;
		Item6 = item6;
		Item7 = item7;
		Item8 = item8;
		Item9 = item9;
		Item10 = item10;
		Item11 = item11;
		Item12 = item12;
	}

	public override bool Equals(object obj) => obj is VolumetricLightingData other && linearToVolumetricScale == other.linearToVolumetricScale && Item2 == other.Item2 && Item3 == other.Item3 && Item4 == other.Item4 && Item5 == other.Item5 && Item6 == other.Item6 && Item7 == other.Item7 && Item8 == other.Item8 && Item9 == other.Item9 && Item10 == other.Item10 && Item11 == other.Item11 && Item12 == other.Item12;

	public override int GetHashCode()
	{
		var hash = new System.HashCode();
		hash.Add(linearToVolumetricScale);
		hash.Add(Item2);
		hash.Add(Item3);
		hash.Add(Item4);
		hash.Add(Item5);
		hash.Add(Item6);
		hash.Add(Item7);
		hash.Add(Item8);
		hash.Add(Item9);
		hash.Add(Item10);
		hash.Add(Item11);
		hash.Add(Item12);
		return hash.ToHashCode();
	}

	public void Deconstruct(out float linearToVolumetricScale, out float item2, out float item3, out float item4, out float item5, out float item6, out float item7, out float item8, out uint item9, out float item10, out uint item11, out float item12)
	{
		linearToVolumetricScale = this.linearToVolumetricScale;
		item2 = Item2;
		item3 = Item3;
		item4 = Item4;
		item5 = Item5;
		item6 = Item6;
		item7 = Item7;
		item8 = Item8;
		item9 = Item9;
		item10 = Item10;
		item11 = Item11;
		item12 = Item12;
	}

	public static implicit operator (float linearToVolumetricScale, float, float, float, float, float, float, float, uint, float, uint, float)(VolumetricLightingData value) => (value.linearToVolumetricScale, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.Item7, value.Item8, value.Item9, value.Item10, value.Item11, value.Item12);
	public static implicit operator VolumetricLightingData((float linearToVolumetricScale, float, float, float, float, float, float, float, uint, float, uint, float) value) => new VolumetricLightingData(value.linearToVolumetricScale, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.Item7, value.Item8, value.Item9, value.Item10, value.Item11, value.Item12);
}