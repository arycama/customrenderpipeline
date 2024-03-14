using System.Collections.Generic;
using UnityEngine.Assertions;

namespace Arycama.CustomRenderPipeline
{
    public class RenderResourceMap
    {
        private readonly Dictionary<string, RenderResourceHandle> handleIndexMap = new();
        private readonly List<IRenderResource> handleList = new();

        public RenderResourceHandle GetResourceHandle(string name)
        {
            if(!handleIndexMap.TryGetValue(name, out var handle))
            {
                handle = new(handleIndexMap.Count);
                handleIndexMap.Add(name, handle);
            }

            handleList.Add(null);
            return handle;
        }

        public T GetRenderResourceHandle<T>(RenderResourceHandle handle) where T : IRenderResource
        {
            var result = handleList[handle.Index];
            Assert.IsTrue(result != null);
            return (T)result;
        }

        public void SetRenderResourceHandle(RenderResourceHandle handle, IRenderResource renderResource)
        {
            handleList[handle.Index] = renderResource;
        }
    }
}
