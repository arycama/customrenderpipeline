using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderPass
    {
        public RenderGraphPass pass;

        private List<Action<CommandBuffer>> preRender = new();
        private List<Action<CommandBuffer>> postRender = new();

        public abstract void SetTexture(CommandBuffer command, string propertyName, Texture texture);
        public abstract void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer);
        public abstract void SetVector(CommandBuffer command, string propertyName, Vector4 value);
        public abstract void SetFloat(CommandBuffer command, string propertyName, float value);
        public abstract void SetInt(CommandBuffer command, string propertyName, int value);
        public abstract void Execute(CommandBuffer command);

        public void ReadTexture(string propertyName, RTHandle texture)
        {
            preRender.Add(cmd => SetTexture(cmd, propertyName, texture));
        }

        public void Run(CommandBuffer command, ScriptableRenderContext context)
        {
            foreach (var cmd in preRender)
                cmd(command);

            preRender.Clear();

            pass(command, context);

            foreach (var cmd in postRender)
                cmd(command);

            postRender.Clear();
        }

        public void SetRenderFunction(RenderGraphPass pass)
        {
            this.pass = pass;
        }
    }
}