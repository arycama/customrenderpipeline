using UnityEngine.Rendering;

public interface IRenderFunction
{
	public abstract void Execute(CommandBuffer command, RenderPass pass, object data);
}
