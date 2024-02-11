using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct RTHandleBindingData
    {
        public RTHandle Handle { get; }
        public RenderBufferLoadAction LoadAction { get; }
        public RenderBufferStoreAction StoreAction { get; }
        public Color ClearColor { get; }
        public float ClearDepth { get; }
        public RenderTargetFlags Flags { get; }
        public int NameId { get; }
        public CubemapFace CubemapFace { get; }
        public int DepthSlice { get; }

        public RTHandleBindingData(RTHandle handle, RenderBufferLoadAction loadAction = RenderBufferLoadAction.DontCare, RenderBufferStoreAction storeAction = RenderBufferStoreAction.DontCare, Color clearColor = default, float clearDepth = 1.0f, RenderTargetFlags flags = RenderTargetFlags.None, int nameId = 0, CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = 0)
        {
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
            LoadAction = loadAction;
            StoreAction = storeAction;
            ClearColor = clearColor;
            ClearDepth = clearDepth;
            Flags = flags;
            NameId = nameId;
            CubemapFace = cubemapFace;
            DepthSlice = depthSlice;
        }
    }
}