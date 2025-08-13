using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class SmoothnessFilterImporter : AssetPostprocessor
{
	[MenuItem("Assets/Texture/Filter Roughness", true)]
	public static bool OnMenuSelectValidate()
	{
		var selection = Selection.activeObject;
		return selection != null && selection is Texture;
	}

	[MenuItem("Assets/Texture/Filter Roughness", false)]
	public static void OnMenuSelect()
	{
		var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(Selection.activeObject)) as TextureImporter;
		var properties = importer.userData;

		if (properties.Contains("RoughnessFilter"))
		{
			properties = null;
			Debug.Log("Disabled Roughness Filtering");
		}
		else
		{
			properties = "RoughnessFilter";
			Debug.Log("Enabled Roughness Filtering");
		}

		importer.userData = properties;
		importer.SaveAndReimport();
	}


	public void OnPostprocessTexture(Texture2D texture)
    {
        if (assetImporter.userData != "RoughnessFilter")
            return;

        // Need to Apply the texture first so it is available for rendering
        texture.Apply();

        var width = texture.width;
        var height = texture.height;

        // First pass will shorten normal based on the average normal length from the smoothness
        var lengthToSmoothness = new RenderTexture(256, 1, 0, RenderTextureFormat.R16)
        {
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Length to Smoothness",
        }.Created();

        var computeShader = Resources.Load<ComputeShader>("SmoothnessFilter");
        var generateLengthToSmoothnessKernel = computeShader.FindKernel("GenerateLengthToSmoothness");
        computeShader.SetFloat("_MaxIterations", 256);
        computeShader.SetFloat("_Resolution", 256);
        computeShader.SetTexture(generateLengthToSmoothnessKernel, "_LengthToRoughnessResult", lengthToSmoothness);
        computeShader.DispatchNormalized(generateLengthToSmoothnessKernel, 256, 1, 1);

        // Intermediate texture for normals, use full float precision
        var temp = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            useMipMap = true
        }.Created();

        // First pass will shorten normal based on the average normal length from the smoothness
        var shortenNormalKernel = computeShader.FindKernel("ShortenNormal");
        computeShader.SetTexture(shortenNormalKernel, "Input", texture);
        computeShader.SetTexture(shortenNormalKernel, "Result", temp, 0);
        computeShader.DispatchNormalized(shortenNormalKernel, width, height, 1);

		// Generate mips for the intermediate normal texture, these will be weighted by the normal lengths
		temp.GenerateMips();

        // Textures to store the results, these will be copied into the final Texture2Ds
        var result = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            useMipMap = true
        }.Created();

        // For each mip (Except mip0 which is unchanged), convert the shortenedNormal (from normalInput) back to smoothness
        // Then normalize and re-pack the normal, and output the final smoothness value
        var mipNormalAndSmoothnessKernel = computeShader.FindKernel("MipNormalAndSmoothness");
        computeShader.SetTexture(mipNormalAndSmoothnessKernel, "_LengthToRoughness", lengthToSmoothness);
		computeShader.SetTexture(mipNormalAndSmoothnessKernel, "Input", temp);

		var mipCount = texture.mipmapCount;
        for (var i = 0; i < mipCount; i++)
        {
            var mipWidth = width >> i;
            var mipHeight = height >> i;

            computeShader.SetInt("_Mip", i);
            computeShader.SetTexture(mipNormalAndSmoothnessKernel, "Result", result, i);
            computeShader.DispatchNormalized(mipNormalAndSmoothnessKernel, mipWidth, mipHeight, 1);
        }

        var mips = result.mipmapCount;
        for (var j = 0; j < mips; j++)
        {
            var normalRequest = AsyncGPUReadback.Request(result, j);
            normalRequest.WaitForCompletion();
            var normalData = normalRequest.GetData<Color32>();
            texture.SetPixelData(normalData, j);
        }

        Object.DestroyImmediate(result);
        Object.DestroyImmediate(temp);
        Object.DestroyImmediate(lengthToSmoothness);
    }
}