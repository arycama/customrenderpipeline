using System.Collections.Generic;
using UnityEngine.Rendering;

public readonly struct ShadowRequestsData : IRenderPassData
{
	public readonly List<ShadowRequest> directionalShadowRequests;
	public readonly List<ShadowRequest> pointShadowRequests;
	public readonly List<ShadowRequest> spotShadowRequests;

	public ShadowRequestsData(List<ShadowRequest> directionalShadowRequests, List<ShadowRequest> pointShadowRequests, List<ShadowRequest> spotShadowRequests)
	{
		this.directionalShadowRequests = directionalShadowRequests;
		this.pointShadowRequests = pointShadowRequests;
		this.spotShadowRequests = spotShadowRequests;
	}

	public void SetInputs(RenderPass pass)
	{
	}

	public void SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}
