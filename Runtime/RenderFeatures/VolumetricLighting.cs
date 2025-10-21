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
		var volumetricLightingData = renderGraph.SetConstantBuffer((
			linearToVolumetricScale,
			-Log2(camera.nearClipPlane) * linearToVolumetricScale,
			(Log2(settings.MaxDistance) - Log2(camera.nearClipPlane)) / settings.DepthSlices,
			Log2(camera.nearClipPlane),
			(float)volumeWidth,
			(float)volumeHeight,
			(float)settings.DepthSlices,
            Rcp(settings.DepthSlices),
            (uint)Log2(settings.TileSize),
            (float)settings.BlurSigma,
			(uint)settings.DepthSlices,
			0f
        ));

		var rawJitter = renderGraph.GetResource<TemporalAASetupData>().Jitter;
		var jitter = 2.0f * rawJitter / new Vector2(camera.scaledPixelWidth, camera.scaledPixelHeight);
		var tanHalfFov = Tan(0.5f * Radians(camera.fieldOfView));
		var pixelToWorldViewDir = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(volumeWidth, volumeHeight, jitter, tanHalfFov, camera.aspect, Matrix4x4.Rotate(camera.transform.rotation));

        var computeShader = Resources.Load<ComputeShader>("VolumetricLighting");
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Volumetric Lighting"))
        {
            pass.Initialize(computeShader, 0, volumeWidth, volumeHeight, settings.DepthSlices);
            pass.WriteTexture("Result", current);

            pass.ReadTexture("Input", history);
			pass.ReadBuffer("VolumetricLightingData", volumetricLightingData);

            pass.AddRenderPassData<ClusteredLightCulling.Result>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowData>();
            pass.AddRenderPassData<CloudShadowDataResult>();
            pass.AddRenderPassData<ViewData>();
            pass.ReadRtHandle<HiZMaxDepth>();
            pass.AddRenderPassData<ParticleShadowData>();

			pass.SetRenderFunction((pixelToWorldViewDir, history), static (command, pass, data) =>
            {
                pass.SetMatrix("PixelToWorldViewDir", data.pixelToWorldViewDir);
				pass.SetVector("InputScale", pass.GetScale3D(data.history));
                pass.SetVector("InputMax", pass.GetLimit3D(data.history));
            });
        }

		// Filter X
		var finalInput = current;
		if (settings.BlurSigma > 0)
		{
			var filterX = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, settings.DepthSlices, TextureDimension.Tex3D);
			using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Filter X"))
			{
				pass.Initialize(computeShader, 1, volumeWidth, volumeHeight, settings.DepthSlices);
				pass.WriteTexture("Result", filterX);
				pass.ReadBuffer("VolumetricLightingData", volumetricLightingData);
				pass.ReadTexture("Input", current);
				pass.ReadRtHandle<HiZMaxDepth>();
			}

			// Filter Y
			var filterY = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, settings.DepthSlices, TextureDimension.Tex3D);
			using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Filter Y"))
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
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Accumulate"))
        {
            pass.Initialize(computeShader, 3, volumeWidth, volumeHeight, 1);
            pass.WriteTexture("Result", volumetricLight);
			pass.ReadBuffer("VolumetricLightingData", volumetricLightingData);
			pass.ReadTexture("Input", finalInput);
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<ViewData>();
			pass.ReadRtHandle<HiZMaxDepth>();

			pass.SetRenderFunction(pixelToWorldViewDir, static (command, pass, data) =>
            {
                pass.SetMatrix("PixelToWorldViewDir", data);
            });
        }

		var result = new Result(volumetricLight, volumetricLightingData);
		renderGraph.SetResource(result);

		renderGraph.AddProfileEndPass("Volumetric Lighting");
	}
}