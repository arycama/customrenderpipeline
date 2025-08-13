using UnityEngine.Rendering;

public interface IRenderPassData
{
	public void SetInputs(RenderPassBase pass);
	public void SetProperties(RenderPassBase pass, CommandBuffer command);
}