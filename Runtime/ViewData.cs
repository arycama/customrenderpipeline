using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public struct ViewData : IRenderPassData
    {
        public Vector3 ViewPosition { get; }
        public float ViewHeight { get; }
        public Quaternion ViewRotation { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public int ScaledWidth { get; }
        public int ScaledHeight { get; }
        public float FieldOfView { get; }
        public float Scale { get; }

        public ViewData(Vector3 viewPosition, float viewHeight, Quaternion viewRotation, int pixelWidth, int pixelHeight, int scaledWidth, int scaledHeight, float fieldOfView, float scale)
        {
            ViewPosition = viewPosition;
            ViewHeight = viewHeight;
            ViewRotation = viewRotation;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            ScaledWidth = scaledWidth;
            ScaledHeight = scaledHeight;
            FieldOfView = fieldOfView;
            Scale = scale;
        }

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}