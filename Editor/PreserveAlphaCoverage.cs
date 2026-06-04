using UnityEditor;
using UnityEngine;
using Unmath;
using static Unmath.Math;

public class PreserveAlphaCoverage : AssetPostprocessor
{
	private const string key = "PreserveCoverage";

	[MenuItem("Assets/Texture/Toggle Preserve Coverage", true)]
	public static bool OnMenuSelectValidate()
	{
		var selection = Selection.activeObject;
		return selection != null && selection is Texture;
	}

	[MenuItem("Assets/Texture/Toggle Preserve Coverage", false)]
	public static void OnMenuSelect()
	{
		var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(Selection.activeObject)) as TextureImporter;
		importer.userData = importer.userData == key ? null : key;
		importer.SaveAndReimport();
	}

	private float CalculateCoverage(float alphaRef, int width, int height, Color[] texels, float alphaScale = 1.0f)
	{
#if true
		var visibleTexels = 0;
		foreach (var texel in texels)
			if (texel.r * alphaScale >= alphaRef)
				visibleTexels++;

		return visibleTexels / (float)texels.Length;
#else
		var coverage = 0.0f;
		var n = 4;
		for (var y = 0; y < height - 1; y++)
		{
			for (var x = 0; x < width - 1; x++)
			{
				var alpha00 = texels[(x + 0) + (y + 0) * width].r * alphaScale;
				var alpha10 = texels[(x + 1) + (y + 0) * width].r * alphaScale;
				var alpha01 = texels[(x + 0) + (y + 1) * width].r * alphaScale;
				var alpha11 = texels[(x + 1) + (y + 1) * width].r * alphaScale;

				var texel_coverage = 0.0f;
				for (var sy = 0; sy < n; sy++)
				{
					var fy = (sy + 0.5f) / n;
					for (var sx = 0; sx < n; sx++)
					{
						var fx = (sx + 0.5f) / n;
						var alpha = alpha00 * (1 - fx) * (1 - fy) + alpha10 * fx * (1 - fy) + alpha01 * (1 - fx) * fy + alpha11 * fx * fy;
						if (alpha > alphaRef)
							texel_coverage += 1.0f;
					}
				}

				coverage += texel_coverage / (n * n);
			}
		}

		return coverage / ((width - 1) * (height - 1));
#endif
	}

	private void ProcessMip(float alphaRef, float desiredCoverage, int mipWidth, int mipHeight, Color[] texels)
	{
		var minAlphaScale = 0.0f;
		var maxAlphaScale = 4.0f;
		var alphaScale = 1.0f;
		var bestAlphaScale = 1.0f;
		var bestError = float.MaxValue;

		for (var i = 0; i < 10; i++)
		{
			var currentCoverage = CalculateCoverage(alphaRef, mipWidth, mipHeight, texels, alphaScale);

			var error = Abs(currentCoverage - desiredCoverage);
			if (error < bestError)
			{
				bestError = error;
				bestAlphaScale = alphaScale;
			}

			if (currentCoverage < desiredCoverage)
			{
				minAlphaScale = alphaScale;
			}
			else if (currentCoverage > desiredCoverage)
			{
				maxAlphaScale = alphaScale;
			}
			else
			{
				break;
			}

			alphaScale = (minAlphaScale + maxAlphaScale) * 0.5f;
		}

		// Re-map alpha values to account for the new cutoff
		for (var j = 0; j < texels.Length; j++)
			texels[j] *= bestAlphaScale;
	}

	private void OnPostprocessTexture(Texture2D texture)
	{
		var importer = assetImporter as TextureImporter;
		if (importer.userData != key)
			return;

		Debug.Log($"Processing preserve coverage for {texture}");

		var desiredCoverage = CalculateCoverage(importer.alphaTestReferenceValue, texture.width, texture.height, texture.GetPixels(0));
		for (var i = 1; i < texture.mipmapCount; i++)
		{
			var texels = texture.GetPixels(i);
			ProcessMip(importer.alphaTestReferenceValue, desiredCoverage, Max(1, texture.width >> i), Max(1, texture.height >> i), texels);
			texture.SetPixels(texels, i);
		}
	}

	private void OnPostprocessTexture2DArray(Texture2DArray texture)
	{
		var importer = assetImporter as TextureImporter;
		if (importer.userData != key)
			return;

		Debug.Log($"Processing preserve coverage for {texture}");

		for (var i = 0; i < texture.depth; i++)
		{
			var desiredCoverage = CalculateCoverage(importer.alphaTestReferenceValue, texture.width, texture.height, texture.GetPixels(i, 0));
			for (var j = 1; j < texture.mipmapCount; j++)
			{
				var texels = texture.GetPixels(i, j);
				ProcessMip(importer.alphaTestReferenceValue, desiredCoverage, Max(1, texture.width >> j), Max(1, texture.height >> j), texels);
				texture.SetPixels(texels, i, j);
			}
		}
	}
}
