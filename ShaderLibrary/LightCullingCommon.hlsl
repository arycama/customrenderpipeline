#include "LightingCommon.hlsl"
#include "Math.hlsl"
#include "MatrixUtils.hlsl"

uint GetThreadsPerTile();
float2 PixelToClipPosition(float2 pixel);
float GetNearPlane();
matrix GetViewToClip(uint viewIndex);
void WriteResult(uint result, uint index);

// Ref https://jcgt.org/published/0002/02/05/paper.pdf
// TODO: This can still be simplified/optimised further
void ViewSphereBounds(float3 axis, float3 center, float radius, float nearZ, out float3 L, out float3 U)
{
    // given in coordinates (a,z), where a is in the direction of the vector a, and z is in the standard z direction
	float2 c = float2(dot(axis, center), center.z);

	// 3.1 Solve for U and L
	float cSquared = dot(c, c);
	float tSquared = cSquared - Sq(radius);
	float t = sqrt(tSquared);
	float2 sinCosTheta = float2(radius, t) * rsqrt(cSquared);
	float2x2 rotate = float2x2(sinCosTheta.yx, -sinCosTheta.x, sinCosTheta.y);
	float k = sqrt(Sq(radius) - Sq(nearZ - c.y));
	
	// (cos, sin) of angle theta between c and a tangent vector
	bool cameraInsideSphere = tSquared <= 0.0;
	if (!cameraInsideSphere)
	{
		U = mul(rotate, c).xxy * sinCosTheta.y;
		L = mul(c, rotate).xxy * sinCosTheta.y;
	}
	
	// Does the near plane intersect the sphere?
	if (c.y + radius > nearZ)
	{
		// Square root of the discriminant. NaN (and unused) if the camera is in the sphere
		if (cameraInsideSphere || U.z < nearZ)
			U = float2(c.x + k, nearZ).xxy;
		
		if (cameraInsideSphere || L.z < nearZ)
			L = float2(c.x - k, nearZ).xxy;
	}
	
	// Transform back to camera space
	float maxZ = max(nearZ, c.y - radius);
	U = float3(U.xy * axis.xy / U.z, 1.0) * maxZ;
	L = float3(L.xy * axis.xy / L.z, 1.0) * maxZ;
}

void LightCulling(uint3 id, uint2 groupId)
{
	// TODO: Precompute where possible
	float4 tilePixelBounds = float4(groupId * TileSize, (groupId + 1) * TileSize);
	
	float4 tileBounds; // (left, up, right, down)
	tileBounds.xy = PixelToClipPosition(tilePixelBounds.xy);
	tileBounds.zw = PixelToClipPosition(tilePixelBounds.zw);
	tileBounds = WaveReadLaneFirst(tileBounds);

	uint laneIndex = WaveGetLaneIndex();
	
	// TODO: Maybe hardcode iteration count for special cases (Eg 1, 2)
	for (uint i = 0; i < LightIndexCount; i++)
	{
		uint index = i * GetThreadsPerTile() + laneIndex;
		
		bool isVisible = false;
		if (index < LightCount)
		{
			Light light = PointLights[index];
		
			// Check if culled by near plane
			float near = GetNearPlane();
			if (light.cullingSphere.z + light.cullingSphere.w > near)
			{
				// TODO: Do this once per light either in a seperate compute shader or on CPU
				float3 left, right, down, up;
				ViewSphereBounds(float3(1, 0, 0), light.cullingSphere.xyz, light.cullingSphere.w, near, left, right);
				ViewSphereBounds(float3(0, 1, 0), light.cullingSphere.xyz, light.cullingSphere.w, near, down, up);
		
				// TODO: Stereo
				matrix viewToClip = GetViewToClip(id.z);
				
				float4 lightBounds;
				lightBounds.x = MultiplyPointProj(viewToClip, left).x;
				lightBounds.y = MultiplyPointProj(viewToClip, down).y;
				lightBounds.z = MultiplyPointProj(viewToClip, right).x;
				lightBounds.w = MultiplyPointProj(viewToClip, up).y;
				
				// TODO: Handle this better
				if (viewToClip._m11 < 0.0)
				{
					if (all(lightBounds.xw < tileBounds.zy && lightBounds.zy > tileBounds.xw))
						isVisible = true;
				}
				else
				{
					lightBounds.yw = -lightBounds.yw;
			
					if (all(lightBounds.xw < tileBounds.zw && lightBounds.zy > tileBounds.xy))
						isVisible = true;
				}
			}
		}
			
		uint visibleBits = WaveActiveBallot(isVisible).x;
		
		if (laneIndex)
			continue;
			
		uint tileIndex = groupId.y * TileCountX + groupId.x;
		WriteResult(visibleBits, tileIndex * LightIndexCount + i);
	}
}