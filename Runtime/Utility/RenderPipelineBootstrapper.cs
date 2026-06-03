using UnityEngine;

namespace CustomRenderPipeline
{
    public static class RenderPipelineBootstrapper
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            RenderPipelineDependencyResolver.AddGlobalDependency(new ProceduralGenerationController());
        }
    }
}