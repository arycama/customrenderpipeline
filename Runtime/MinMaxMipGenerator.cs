using UnityEngine;

[ExecuteAlways]
public class MinMaxMipGenerator : MonoBehaviour
{
	public Texture2D inputHeightmap;
	public ComputeShader minMaxMipComputeShader;
	public RenderTexture minMaxMipmap;
	public Material material;

	public static int DivRoundUp(int x, int y)
	{
		return (x + y - 1) / y;
	}

	void OnEnable()
	{
		var width = inputHeightmap.width;
		var height = inputHeightmap.height;
		var mipCount = Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2)) + 1;

		// Create a RenderTexture with mipmaps
		minMaxMipmap = new RenderTexture(width, height, 0, RenderTextureFormat.R8);
		minMaxMipmap.enableRandomWrite = true;
		minMaxMipmap.useMipMap = true;
		minMaxMipmap.autoGenerateMips = false;
		_ = minMaxMipmap.Create();

		// Generate each mip level
		for (var i = 0; i < mipCount; i++)
		{
			var mipWidth = Mathf.Max(1, width >> i);
			var mipHeight = Mathf.Max(1, height >> i);
			minMaxMipComputeShader.SetInt("Width", mipWidth);
			minMaxMipComputeShader.SetInt("Height", mipHeight);
			minMaxMipComputeShader.SetInt("Mip", i - 1);

			var kernel = i == 0 ? 0 : 1;
			if(kernel == 0)
				minMaxMipComputeShader.SetTexture(kernel, "Input", inputHeightmap);
			else
				minMaxMipComputeShader.SetTexture(kernel, "MipInput", minMaxMipmap, i - 1);

			minMaxMipComputeShader.SetTexture(kernel, "Output", minMaxMipmap, i);
			minMaxMipComputeShader.Dispatch(kernel, DivRoundUp(mipWidth, 8), DivRoundUp(mipHeight, 8), 1);
		}

		material.SetTexture("Height", minMaxMipmap);
	}
}