using UnityEngine;
using UnityEngine.Rendering;

public class TemporalAASetup : CameraRenderFeature
{
	private readonly TemporalAA.Settings settings;

	public TemporalAASetup(RenderGraph renderGraph, TemporalAA.Settings settings) : base(renderGraph)
	{
		this.settings = settings;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var sampleIndex = renderGraph.FrameIndex % settings.SampleCount + 1;

		Vector2 jitter;
		jitter.x = Halton(sampleIndex, 2) - 0.5f;
		jitter.y = Halton(sampleIndex, 3) - 0.5f;

		jitter *= settings.JitterSpread;

		var previousSampleIndex = Math.Max(0, renderGraph.FrameIndex - 1) % settings.SampleCount + 1;

		Vector2 previousJitter;
		previousJitter.x = Halton(previousSampleIndex, 2) - 0.5f;
		previousJitter.y = Halton(previousSampleIndex, 3) - 0.5f;

		previousJitter *= settings.JitterSpread;

		if (settings.JitterOverride)
			jitter = settings.JitterOverrideValue;

		if (!settings.IsEnabled)
			jitter = previousJitter = Vector2.zero;

		var weights = ArrayPool<float>.Get(9);
		float boxWeightSum = 0.0f, crossWeightSum = 0.0f;
		float maxCrossWeight = 0.0f, maxBoxWeight = 0.0f;
		for (int y = -1, i = 0; y <= 1; y++)
		{
			for (var x = -1; x <= 1; x++, i++)
			{
				var weight = Filter(x - jitter.x, y - jitter.y);

				if (!settings.IsEnabled)
					weight = (x == 0 && y == 0) ? 1.0f : 0.0f;

				weights[i] = weight;
				boxWeightSum += weight;
				maxBoxWeight = Mathf.Max(maxBoxWeight, weight);

				if (x == 0 || y == 0)
				{
					crossWeightSum += weight;
					maxCrossWeight = Mathf.Max(maxCrossWeight, weight);
				}
			}
		}

		// Normalize weights
		var rcpCrossWeightSum = 1.0f / crossWeightSum;
		var rcpBoxWeightSum = 1.0f / boxWeightSum;

		renderGraph.SetResource(new TemporalAAData
		(
			renderGraph.SetConstantBuffer((
				new Vector4(jitter.x, jitter.y, jitter.x / camera.scaledPixelWidth, jitter.y / camera.scaledPixelHeight),
				new Vector4(previousJitter.x, previousJitter.y, previousJitter.x / camera.scaledPixelWidth, previousJitter.y / camera.scaledPixelHeight),
				crossWeightSum,
				boxWeightSum,
				weights[4] * rcpCrossWeightSum,
				weights[4] * rcpBoxWeightSum,
				new Vector4(weights[1], weights[3], weights[5], weights[7]) * rcpCrossWeightSum,
				new Vector4(weights[0], weights[1], weights[2], weights[3]) * rcpBoxWeightSum,
				new Vector4(weights[5], weights[6], weights[7], weights[8]) * rcpBoxWeightSum))
		)); ;

		ArrayPool<float>.Release(weights);

		renderGraph.SetResource(new TemporalAASetupData(jitter));
	}

	public static float Halton(int index, int radix)
	{
		var result = 0f;
		var fraction = 1f / radix;

		while (index > 0)
		{
			result += index % radix * fraction;

			index /= radix;
			fraction /= radix;
		}

		return result;
	}

	private float Mitchell1D(float x)
	{
		var B = settings.SpatialBlur;
		var C = settings.SpatialSharpness;
		x = Mathf.Abs(x) * settings.SpatialSize;

		if (x <= 1.0f)
			return ((12 - 9 * B - 6 * C) * x * x * x + (-18 + 12 * B + 6 * C) * x * x + (6 - 2 * B)) * (1.0f / 6.0f);
		else if (x <= 2.0f)
			return ((-B - 6 * C) * x * x * x + (6 * B + 30 * C) * x * x + (-12 * B - 48 * C) * x + (8 * B + 24 * C)) * (1.0f / 6.0f);
		else
			return 0.0f;
	}

	private float Filter(float x, float y)
	{
		//return Math.Saturate(1 - Math.Abs(x)) * Math.Saturate(1 - Math.Abs(y));

		//var p = settings.SpatialSharpness;
		//var k = 1.0f / (Mathf.Pow(4.0f / 3.0f, p) - 1.0f);

		//var cx = 1.0f - Mathf.Min(1, Mathf.Pow(x * 2.0f / 3.0f, 2.0f));
		//var ax = ((1 + k) * Mathf.Pow(cx, p) - k) * cx * cx;

		//var cy = 1.0f - Mathf.Min(1, Mathf.Pow(y * 2.0f / 3.0f, 2.0f));
		//var ay = ((1 + k) * Mathf.Pow(cy, p) - k) * cy * cy;

		//return ax * ay;
		return Mitchell1D(x) * Mitchell1D(y);
	}
}