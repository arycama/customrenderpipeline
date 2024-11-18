using System;
using System.Collections.Generic;
using UnityEngine.Assertions;

namespace Arycama.CustomRenderPipeline
{
    public class RenderResourceMap : IDisposable
    {
        private readonly Dictionary<Type, RenderPassDataHandle> handleIndexMap = new();
        private readonly List<(IRenderPassData data, int frameIndex, bool isPersistent)> handleList = new();
        private bool disposedValue;

        // TODO: Should have a tryget method which fails if not already initialized? (Thoguh we should keep this one so that types can prefetch handles out of order
        public RenderPassDataHandle GetResourceHandle<T>() where T : IRenderPassData
        {
            if (!handleIndexMap.TryGetValue(typeof(T), out var handle))
            {
                handle = new(handleIndexMap.Count);
                handleIndexMap.Add(typeof(T), handle);
                handleList.Add((null, 0, false));
            }

            return handle;
        }

        public T GetRenderPassData<T>(RenderPassDataHandle handle, int frameIndex) where T : IRenderPassData
        {
            var result = handleList[handle.Index];
            Assert.IsNotNull(result.Item1, "Data has not been set for type");
            Assert.IsTrue(result.isPersistent || (result.Item2 == frameIndex), "Getting non-persistent renderdata for a previous frame");
            return (T)result.Item1;
        }

        public bool TryGetRenderPassData<T>(RenderPassDataHandle handle, int frameIndex, out T data) where T : IRenderPassData
        {
            var result = handleList[handle.Index];

            if (frameIndex == result.Item2 && result.Item1 != null)
            {
                data = (T)result.Item1;
                return true;
            }

            data = default(T);
            return false;
        }

        public bool IsRenderPassDataValid<T>(RenderPassDataHandle handle, int frameIndex) where T : IRenderPassData
        {
            var result = handleList[handle.Index];

            if (frameIndex == result.Item2 && result.Item1 != null)
            {
                return true;
            }

            return false;
        }

        public bool IsRenderPassDataValid<T>(int frameIndex) where T : IRenderPassData
        {
            var handle = GetResourceHandle<T>();
            var result = handleList[handle.Index];

            if (frameIndex == result.Item2 && result.Item1 != null)
            {
                return true;
            }

            return false;
        }

        public T GetRenderPassData<T>(int frameIndex) where T : IRenderPassData
        {
            var handle = GetResourceHandle<T>();
            return GetRenderPassData<T>(handle, frameIndex);
        }

        public void SetRenderPassData(RenderPassDataHandle handle, IRenderPassData renderResource, int frameIndex, bool isPersistent = false)
        {
            handleList[handle.Index] = (renderResource, frameIndex, isPersistent);
        }

        public void SetRenderPassData<T>(T renderResource, int frameIndex, bool isPersistent = false) where T : IRenderPassData
        {
            var handle = GetResourceHandle<T>();
            SetRenderPassData(handle, renderResource, frameIndex, isPersistent);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    handleIndexMap.Clear();
                    handleList.Clear();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
