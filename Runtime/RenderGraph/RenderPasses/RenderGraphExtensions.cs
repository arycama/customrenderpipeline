public static class RenderGraphExtensions
{
	// Typed
	public static BlitToScreenPass<T> AddBlitToScreenPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<BlitToScreenPass<T>, T>(name, data);
	public static ComputeRenderPass<T> AddComputeRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<ComputeRenderPass<T>, T>(name, data);
	public static DrawInstancedIndirectRenderPass<T> AddDrawInstancedIndirectRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawInstancedIndirectRenderPass<T>, T>(name, data);
	public static DrawProceduralIndexedRenderPass<T> AddDrawProceduralIndexedRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawProceduralIndexedRenderPass<T>, T>(name, data);
	public static DrawProceduralIndirectIndexedRenderPass<T> AddDrawProceduralIndirectIndexedRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawProceduralIndirectIndexedRenderPass<T>, T>(name, data);
	public static DrawProceduralIndirectRenderPass<T> AddDrawProceduralIndirectRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass<T>, T>(name, data);
	public static DrawProceduralRenderPass<T> AddDrawProceduralRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawProceduralRenderPass<T>, T>(name, data);
	public static FullscreenRenderPass<T> AddFullscreenRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<FullscreenRenderPass<T>, T>(name, data);
	public static GenericRenderPass<T> AddGenericRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<GenericRenderPass<T>, T>(name, data);
	public static IndirectComputeRenderPass<T> AddIndirectComputeRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<IndirectComputeRenderPass<T>, T>(name, data);
	public static ObjectRenderPass<T> AddObjectRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<ObjectRenderPass<T>, T>(name, data);
	public static RaytracingRenderPass<T> AddRaytracingRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<RaytracingRenderPass<T>, T>(name, data);
	public static ShadowRenderPass<T> AddShadowRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<ShadowRenderPass<T>, T>(name, data);

	// Defaults (Still need a type to avoid unneccessary class variants)
	public static BlitToScreenPass<int>AddBlitToScreenPass(this RenderGraph renderGraph, string name) => AddBlitToScreenPass(renderGraph, name, 0);
	public static ComputeRenderPass<int>AddComputeRenderPass(this RenderGraph renderGraph, string name) => AddComputeRenderPass(renderGraph, name, 0);
	public static DrawInstancedIndirectRenderPass<int>AddDrawInstancedIndirectRenderPass(this RenderGraph renderGraph, string name) => AddDrawInstancedIndirectRenderPass(renderGraph, name, 0);
	public static DrawProceduralIndexedRenderPass<int>AddDrawProceduralIndexedRenderPass(this RenderGraph renderGraph, string name) => AddDrawProceduralIndexedRenderPass(renderGraph, name, 0);
	public static DrawProceduralIndirectIndexedRenderPass<int>AddDrawProceduralIndirectIndexedRenderPass(this RenderGraph renderGraph, string name) => AddDrawProceduralIndirectIndexedRenderPass(renderGraph, name, 0);
	public static DrawProceduralIndirectRenderPass<int>AddDrawProceduralIndirectRenderPass(this RenderGraph renderGraph, string name) => AddDrawProceduralIndirectRenderPass(renderGraph, name, 0);
	public static DrawProceduralRenderPass<int>AddDrawProceduralRenderPass(this RenderGraph renderGraph, string name) => AddDrawProceduralRenderPass(renderGraph, name, 0);
	public static FullscreenRenderPass<int>AddFullscreenRenderPass(this RenderGraph renderGraph, string name) => AddFullscreenRenderPass(renderGraph, name, 0);
	public static GenericRenderPass<int>AddGenericRenderPass(this RenderGraph renderGraph, string name) => AddGenericRenderPass(renderGraph, name, 0);
	public static IndirectComputeRenderPass<int>AddIndirectComputeRenderPass(this RenderGraph renderGraph, string name) => AddIndirectComputeRenderPass(renderGraph, name, 0);
	public static ObjectRenderPass<int>AddObjectRenderPass(this RenderGraph renderGraph, string name) => AddObjectRenderPass(renderGraph, name, 0);
	public static RaytracingRenderPass<int>AddRaytracingRenderPass(this RenderGraph renderGraph, string name) => AddRaytracingRenderPass(renderGraph, name, 0);
	public static ShadowRenderPass<int>AddShadowRenderPass(this RenderGraph renderGraph, string name) => AddShadowRenderPass(renderGraph, name, 0);
}
