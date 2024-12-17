using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class TimeData : IRenderPassData
    {
        public double Time { get; }
        public double PreviousTime { get; }
        public double DeltaTime { get; }

        public TimeData(double time, double previousTime, double deltaTime)
        {
            Time = time;
            PreviousTime = previousTime;
            DeltaTime = deltaTime;
        }

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}