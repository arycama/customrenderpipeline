using UnityEngine;
using UnityEngine.Rendering;

public interface IRendererStage
{
    void Render(Camera camera, ScriptableRenderContext context);
}