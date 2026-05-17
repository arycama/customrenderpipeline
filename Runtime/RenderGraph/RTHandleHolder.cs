using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public static class RTHandleHolder
{
    private static (ResourceHandle<RenderTexture> handle, int mip, RenderTextureSubElement subElement)[] handleData;

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        var targetType = typeof(IRtHandleId);

        var types = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                     from type in assembly.GetTypes()
                     where targetType.IsAssignableFrom(type)
                     select type).ToArray();

        handleData = new (ResourceHandle<RenderTexture>, int, RenderTextureSubElement)[types.Length];

        var count = 0;
        foreach (var type in types)
        {
            var closedGenericType = typeof(RTHandleHolder<>).MakeGenericType(type);

            var indexFieldInfo = closedGenericType.GetField("index", BindingFlags.Public | BindingFlags.Static);
            indexFieldInfo.SetValue(null, count);

            var propertyNameIdFieldInfo = closedGenericType.GetField("propertyNameId", BindingFlags.Public | BindingFlags.Static);
            var propertyNameId = Shader.PropertyToID(type.Name);
            propertyNameIdFieldInfo.SetValue(null, propertyNameId);

            count++;
        }
    }

    public static void SetHandle<T>(ResourceHandle<RenderTexture> handle, int mip, RenderTextureSubElement subElement) where T : IRtHandleId
    {
        var index = RTHandleHolder<T>.index;
        handleData[index] = (handle, mip, subElement);
    }

    public static (ResourceHandle<RenderTexture> handle, int mip, RenderTextureSubElement subElement) GetHandleData(int index)
    {
        return handleData[index];
    }

    public static ResourceHandle<RenderTexture> GetRtHandle(int index)
    {
        return handleData[index].handle;
    }

    public static ResourceHandle<RenderTexture> GetRtHandle<T>() where T : IRtHandleId
    {
        var index = RTHandleHolder<T>.index;
        return GetRtHandle(index);
    }
}

public static class RTHandleHolder<T> where T : IRtHandleId
{
    public static int index;
    public static int propertyNameId;
}