using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class CustomRenderPipeline : RenderPipeline
    {
        public bool IsDisposingFromRenderDoc { get; protected set; }

        protected sealed override void Render(ScriptableRenderContext context, Camera[] cameras) { }
    }
}
