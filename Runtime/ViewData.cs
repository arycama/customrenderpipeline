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
        public int ViewIndex { get; }
        public float Near { get; }
        public float Far { get; }
        public float Aspect { get; }
        public Vector2 Jitter { get; }

        public ViewData(Vector3 viewPosition, float viewHeight, Quaternion viewRotation, int pixelWidth, int pixelHeight, int scaledWidth, int scaledHeight, float fieldOfView, float scale, int viewIndex, float near, float far, float aspect, Vector2 jitter)
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
            ViewIndex = viewIndex;
            Near = near;
            Far = far;
            Aspect = aspect;
            Jitter = jitter;
        }

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}