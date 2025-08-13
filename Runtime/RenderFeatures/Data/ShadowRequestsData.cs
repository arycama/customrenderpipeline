using System.Collections.Generic;
using UnityEngine.Rendering;

public class ShadowRequestsData : IRenderPassData
{
	public List<ShadowRequest> DirectionalShadowRequests { get; }
	public List<ShadowRequest> PointShadowRequests { get; }
	public List<ShadowRequest> SpotShadowRequests { get; }

	public ShadowRequestsData(List<ShadowRequest> directionalShadowRequests, List<ShadowRequest> pointShadowRequests, List<ShadowRequest> spotShadowRequests)
	{
		DirectionalShadowRequests = directionalShadowRequests;
		PointShadowRequests = pointShadowRequests;
		SpotShadowRequests = spotShadowRequests;
	}

	public void SetInputs(RenderPassBase pass)
	{
	}

	public void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}
