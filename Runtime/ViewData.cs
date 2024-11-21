using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public struct ViewData : IRenderPassData
    {
        public Vector3 ViewPosition { get; }
        public float ViewHeight { get; }
        public Quaternion ViewRotation { get; }

        public ViewData(Vector3 viewPosition, float viewHeight, Quaternion viewRotation)
        {
            ViewPosition = viewPosition;
            ViewHeight = viewHeight;
            ViewRotation = viewRotation;
        }

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}