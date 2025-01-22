using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderPassBase : IDisposable
    {
        protected CommandBuffer Command { get; private set; }
        public RenderGraph RenderGraph { get; set; }
        internal string Name { get; set; }
        internal int Index { get; set; }

        protected abstract void Execute();

        void IDisposable.Dispose()
        {
        }

        public GraphicsBuffer GetBuffer(ResourceHandle<GraphicsBuffer> handle)
        {
            Assert.IsTrue(RenderGraph.IsExecuting);
            return RenderGraph.BufferHandleSystem.GetResource(handle);
        }

        public RenderTexture GetRenderTexture(ResourceHandle<RenderTexture> handle)
        {
            Assert.IsTrue(RenderGraph.IsExecuting);
            return RenderGraph.RtHandleSystem.GetResource(handle);
        }

        public void Run(CommandBuffer command)
        {
            this.Command = command;
            RunInternal();
        }

        protected virtual void RunInternal()
        {
            Execute();
        }
    }
}
