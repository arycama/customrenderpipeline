using System.Collections.Generic;
using UnityEngine;

public static class GraphicsUtilities
{
	public static Float4 ThreadIdScaleOffset(int width, int height)
	{
		return new Float4((float)(1.0 / width), (float)(1.0 / height), (float)(0.5 / width), (float)(0.5 / height));
	}

	/// <summary>
	/// Calculates a scale and offset for remapping a UV from a 0-1 range to a halfTexel to (1-halfTexel) range
	/// </summary>
	public static Float2 HalfTexelRemap(float width)
	{
		var invWidth = 1f / width;
		return new Float2(1f - invWidth, 0.5f * invWidth);
	}

	/// <summary>
	/// Calculates a scale and offset for remapping a UV from a 0-1 range to a halfTexel to (1-halfTexel) range
	/// </summary>
	public static Float4 HalfTexelRemap(float width, float height)
	{
		var invWidth = 1f / width;
		var invHeight = 1f / height;
		return new Float4(1f - invWidth, 1f - invHeight, 0.5f * invWidth, 0.5f * invHeight);
	}

	public static Float4 HalfTexelRemap(Float2 position, Float2 size, Float2 resolution)
	{
		Float4 result;
		result.x = (resolution.x - 1f) / (size.x * resolution.x);
		result.y = (resolution.y - 1f) / (size.x * resolution.y);
		result.z = (0.5f * size.x + position.x - position.x * resolution.x) / (size.x * resolution.x);
		result.w = (0.5f * size.y + position.y - position.y * resolution.y) / (size.y * resolution.y);
		return result;
	}

	/// <summary>
	/// Calculates a scale and offset for remapping a UV from a 0-1 range to a halfTexel to (1-halfTexel) range
	/// </summary>
	public static void HalfTexelRemap(float width, float height, float depth, out Float3 scale, out Float3 offset)
	{
		var invWidth = 1f / width;
		var invHeight = 1f / height;
		var invDepth = 1f / depth;
		scale = new Float3(1f - invWidth, 1f - invHeight, 1f - invDepth);
		offset = new Float3(0.5f * invWidth, 0.5f * invHeight, 0.5f * invDepth);
	}

	public static Float4 RemapHalfTexelTo01(float width, float height)
	{
		Float4 result;
		result.x = width / (width - 1f);
		result.y = height / (height - 1f);
		result.z = -0.5f / (width - 1f);
		result.w = -0.5f / (height - 1f);
		return result;
	}

	/// <summary>
	/// Calculates ScaleOffset to Remap a CS thread to UV coordinate that stretches from 0:1. (No half-texel offset)
	/// </summary>
	public static Float3 ThreadIdScaleOffset01(int width, int height, int depth)
	{
		return new Float3(1f / (width - 1), 1f / (height - 1), 1f / (depth - 1));
	}

	/// <summary>
	/// Calculates ScaleOffset to Remap a CS thread to UV coordinate that stretches from 0:1. (No half-texel offset)
	/// </summary>
	public static Float2 ThreadIdScaleOffset01(int width, int height)
	{
		return new Float2(1f / (width - 1), 1f / (height - 1));
	}

	public static void GenerateGridIndexBuffer(List<ushort> list, int cellsPerRow, bool isQuad, bool alternateIndices)
	{
		var indicesPerQuad = isQuad ? 4 : 6;

		for (int y = 0, i = 0, vi = 0; y < cellsPerRow; y++, vi++)
		{
			var rowStart = y * (cellsPerRow + 1);

			for (var x = 0; x < cellsPerRow; x++, i += indicesPerQuad, vi++)
			{
				var columnStart = rowStart + x;

				var flip = alternateIndices ? (x & 1) == (y & 1) : true;

				if (isQuad)
				{
					if (flip)
					{
						list.Add((ushort)(columnStart));
						list.Add((ushort)(columnStart + cellsPerRow + 1));
						list.Add((ushort)(columnStart + cellsPerRow + 2));
						list.Add((ushort)(columnStart + 1));
					}
					else
					{
						list.Add((ushort)(columnStart + cellsPerRow + 1));
						list.Add((ushort)(columnStart + cellsPerRow + 2));
						list.Add((ushort)(columnStart + 1));
						list.Add((ushort)(columnStart));
					}
				}
				else
				{
					if (flip)
					{
						list.Add((ushort)columnStart);
						list.Add((ushort)(columnStart + cellsPerRow + 1));
						list.Add((ushort)(columnStart + cellsPerRow + 2));
						list.Add((ushort)(columnStart + cellsPerRow + 2));
						list.Add((ushort)(columnStart + 1));
						list.Add((ushort)columnStart);
					}
					else
					{
						list.Add((ushort)columnStart);
						list.Add((ushort)(columnStart + cellsPerRow + 1));
						list.Add((ushort)(columnStart + 1));
						list.Add((ushort)(columnStart + 1));
						list.Add((ushort)(columnStart + cellsPerRow + 1));
						list.Add((ushort)(columnStart + cellsPerRow + 2));
					}
				}
			}
		}
	}

	/// <summary> Generates an index buffer of quads. Eg every 4 or 6 indices will refer to a new quad. (Can support either triangle or quad topology) </summary>
	public static void GenerateQuadIndexBuffer(List<ushort> list, int count, bool isQuad)
	{
		for (var i = 0; i < count; i++)
		{
			if (isQuad)
			{
				list.Add((ushort)(4 * i + 0));
				list.Add((ushort)(4 * i + 1));
				list.Add((ushort)(4 * i + 2));
				list.Add((ushort)(4 * i + 3));
			}
			else
			{
				list.Add((ushort)(4 * i + 0));
				list.Add((ushort)(4 * i + 1));
				list.Add((ushort)(4 * i + 2));
				list.Add((ushort)(4 * i + 0));
				list.Add((ushort)(4 * i + 2));
				list.Add((ushort)(4 * i + 3));
			}
		}
	}

	public static void GenerateQuadIndexBuffer(List<uint> list, int count, bool isQuad)
	{
		for (var i = 0u; i < count; i++)
		{
			if (isQuad)
			{
				list.Add(4u * i + 0u);
				list.Add(4u * i + 1u);
				list.Add(4u * i + 2u);
				list.Add(4u * i + 3u);
			}
			else
			{
				list.Add(4u * i + 0u);
				list.Add(4u * i + 1u);
				list.Add(4u * i + 2u);
				list.Add(4u * i + 0u);
				list.Add(4u * i + 2u);
				list.Add(4u * i + 3u);
			}
		}
	}

	public static void SafeDestroy(ref ComputeBuffer buffer)
	{
		if (buffer != null)
		{
			buffer.Release();
			buffer = null;
		}
	}

	public static void SafeDestroy(ref GraphicsBuffer buffer)
	{
		if (buffer != null)
		{
			buffer.Release();
			buffer = null;
		}
	}

	public static void SafeDestroy<T>(ref T buffer) where T : Object
	{
		if (buffer != null)
		{
			Object.DestroyImmediate(buffer);
			buffer = null;
		}
	}

	public static void SafeResize(ref ComputeBuffer computeBuffer, int size = 1, int stride = sizeof(int), ComputeBufferType type = ComputeBufferType.Default)
	{
		if (computeBuffer == null || computeBuffer.count != size)
		{
			if (computeBuffer != null)
			{
				computeBuffer.Release();
				computeBuffer = null;
			}

			if (size > 0)
				computeBuffer = new ComputeBuffer(size, stride, type);
		}
	}

	public static void SafeExpand(ref ComputeBuffer computeBuffer, int size = 1, int stride = sizeof(int), ComputeBufferType type = ComputeBufferType.Default)
	{
		size = Mathf.Max(size, 1);

		if (computeBuffer == null || computeBuffer.count < size)
		{
			if (computeBuffer != null)
				computeBuffer.Release();

			computeBuffer = new ComputeBuffer(size, stride, type);
		}
	}

	/// <summary> ScaleOffset to remap from one texel range to another. Apply via texel * scaleOffset.xy + scaleOffsetzw </summary>
	public static Float4 TexelRemap(Rect source, Rect dest)
	{
		var remapX = Math.RemapScaleOffset(source.min.x, source.max.x, dest.min.x, dest.max.x);
		var remapY = Math.RemapScaleOffset(source.min.y, source.max.y, dest.min.y, dest.max.y);
		return new Float4(remapX.x, remapY.x, remapX.y, remapY.y);
	}

	/// <summary> Calculates normalized scale offset that would go from a normalized (0 to 1) uv to the normalized coords of the dest rect</summary>
	public static Float4 TexelRemapNormalized(Rect dest, int destResolution)
	{
		var rcp = 1.0f / destResolution;
		return TexelRemap(new Rect(0, 0, 1, 1), new Rect(rcp * dest.x, rcp * dest.y, rcp * dest.width, rcp * dest.height));
	}
}