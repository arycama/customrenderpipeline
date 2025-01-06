using System;
using System.Collections.Generic;

public static class DependencyResolver
{
    private static readonly Dictionary<Type, object> globalDependencies = new();

    public static void AddGlobalDependency<T>(T dependency)
    {
        globalDependencies.Add(typeof(T), dependency);
    }

    public static T Resolve<T>()
    {
        return (T)globalDependencies[typeof(T)];
    }
}
