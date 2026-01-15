using UnityEngine;
using UnityEngine.Rendering;

public class FullscreenRenderPass<T> : DrawRenderPass<T>
{
	private Material material;
	private int passIndex;
	private int primitiveCount;
	private SinglePassStereoMode stereoMode;

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public virtual void Initialize(Material material, int passIndex = 0, int primitiveCount = 1, bool isStereo = false)
	{
		this.material = material;
		this.passIndex = passIndex;
		this.primitiveCount = primitiveCount;

		stereoMode = isStereo
            ? SystemInfo.supportsMultiview ? SinglePassStereoMode.Multiview : SinglePassStereoMode.Instancing
            : SinglePassStereoMode.None;
    }

	public override void Reset()
	{
		base.Reset();
		material = null;
		passIndex = 0;
		primitiveCount = 1;
	}

	protected override void Execute()
	{
		foreach (var keyword in keywords)
			Command.EnableKeyword(material, new LocalKeyword(material.shader, keyword));

        int instanceMultiplier;
        if (stereoMode == SinglePassStereoMode.Instancing)
        {
            Command.EnableShaderKeyword("STEREO_INSTANCING_ON");
            Command.SetSinglePassStereo(SinglePassStereoMode.Instancing);
            instanceMultiplier = 2;
        }
        else
        {
            instanceMultiplier = 1;
            if (stereoMode == SinglePassStereoMode.Multiview)
            {
                Command.EnableShaderKeyword("STEREO_MULTIVIEW_ON");
                Command.SetSinglePassStereo(SinglePassStereoMode.Multiview);
            }
        }

        Command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3 * primitiveCount * instanceMultiplier, 1, PropertyBlock);

        if (stereoMode == SinglePassStereoMode.Instancing)
        {
            Command.SetSinglePassStereo(SinglePassStereoMode.None);
            Command.DisableShaderKeyword("STEREO_INSTANCING_ON");
        }
        else if (stereoMode == SinglePassStereoMode.Multiview)
        {
            Command.SetSinglePassStereo(SinglePassStereoMode.None);
            Command.DisableShaderKeyword("STEREO_MULTIVIEW_ON");
        }

        foreach (var keyword in keywords)
			Command.DisableKeyword(material, new LocalKeyword(material.shader, keyword));
	}
}
