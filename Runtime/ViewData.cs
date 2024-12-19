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

        // TODO: These are possibly temporary
        public Matrix4x4 ClipToWorld { get; }
        public Matrix4x4 JitteredClipToWorld { get; }
        public Camera Camera { get; }

        public ViewData(Vector3 viewPosition, float viewHeight, Quaternion viewRotation, int pixelWidth, int pixelHeight, int scaledWidth, int scaledHeight, float fieldOfView, float scale, int viewIndex, float near, float far, float aspect, Vector2 jitter, Matrix4x4 clipToWorld, Matrix4x4 jitteredClipToWorld, Camera camera)
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
            ClipToWorld = clipToWorld;
            JitteredClipToWorld = jitteredClipToWorld;
            Camera = camera;
        }

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}