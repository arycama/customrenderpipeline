﻿using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class BloomData : RTHandleData
    {
        public BloomData(ResourceHandle<RenderTexture> handle) : base(handle, "_Bloom")
        {
        }
    }
}