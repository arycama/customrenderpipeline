using System;
using System.Collections.Generic;
using UnityEngine.Assertions;

namespace Arycama.CustomRenderPipeline
{
    public class RenderResourceMap
    {
        private readonly Dictionary<Type, RenderPassDataHandle> handleIndexMap = new();
        private readonly List<IRenderPassData> handleList = new();

        public RenderPassDataHandle GetResourceHandle<T>() where T : IRenderPassData
        {
            if(!handleIndexMap.TryGetValue(typeof(T), out var handle))
            {
                handle = new(handleIndexMap.Count);
                handleIndexMap.Add(typeof(T), handle);
            }

            handleList.Add(null);
            return handle;
        }

        public T GetRenderPassData<T>(RenderPassDataHandle handle) where T : IRenderPassData
        {
            var result = handleList[handle.Index];
            //Assert.IsTrue(result != null, $"Unable to get data for type {typeof(T)}");
            Assert.IsTrue(result != null, "Unable to get data for type");
            return (T)result;
        }

        public T GetRenderPassData<T>() where T : IRenderPassData
        {
            var handle = GetResourceHandle<T>();
            return GetRenderPassData<T>(handle);
        }

        public void SetRenderPassData(RenderPassDataHandle handle, IRenderPassData renderResource)
        {
            handleList[handle.Index] = renderResource;
        }

        public void SetRenderPassData<T>(T renderResource) where T : IRenderPassData
        {
            var handle = GetResourceHandle<T>();
            SetRenderPassData(handle, renderResource);
        }
    }
}
