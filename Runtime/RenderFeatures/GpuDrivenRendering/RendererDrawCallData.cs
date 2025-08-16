using System;
using UnityEngine;

public struct RendererDrawCallData
{
    public int renderQueue;
    public Mesh mesh;
    public int submeshIndex;
    public Material material;
    public int passIndex;
    public int indirectArgsOffset;
    public int rendererOffset;
	public int lodOffset;

    public RendererDrawCallData(int renderQueue, Mesh mesh, int submeshIndex, Material material, int passIndex, int indirectArgsOffset, int rendererOffset, int lodOffset)
    {
        this.renderQueue = renderQueue;
        this.mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        this.submeshIndex = submeshIndex;
        this.material = material ?? throw new ArgumentNullException(nameof(material));
        this.passIndex = passIndex;
        this.indirectArgsOffset = indirectArgsOffset;
        this.rendererOffset = rendererOffset;
		this.lodOffset = lodOffset;
    }

	public override string ToString() => $"{mesh} {submeshIndex} {material} {renderQueue} {passIndex} {indirectArgsOffset} {rendererOffset} {lodOffset}";
}