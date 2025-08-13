using UnityEngine;

public static class RenderPipelineBootstrapper
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        DependencyResolver.AddGlobalDependency(new ProceduralGenerationController());
    }
}