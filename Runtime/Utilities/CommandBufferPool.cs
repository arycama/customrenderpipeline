using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public static class CommandBufferPool
    {
        private static readonly ObjectPool<CommandBuffer> bufferPool = new(() => new CommandBuffer(), x => x.Clear());

        public static CommandBuffer Get(string name)
        {
            var cmd = bufferPool.Get();
            cmd.name = name;
            return cmd;
        }

        public static void Release(CommandBuffer buffer)
        {
            bufferPool.Release(buffer);
        }
    }
}