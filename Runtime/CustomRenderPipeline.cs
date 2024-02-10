using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class CustomRenderPipeline : RenderPipeline
    {
        protected sealed override void Render(ScriptableRenderContext context, Camera[] cameras) { }
    }
}
