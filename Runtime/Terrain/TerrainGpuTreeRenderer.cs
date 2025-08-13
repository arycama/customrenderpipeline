using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static Math;

[ExecuteAlways]
public class TerrainGpuTreeRenderer : MonoBehaviour, IGpuProceduralGenerator
{
	private ProceduralGenerationController proceduralGenerationController;

	private ResourceHandle<GraphicsBuffer> positionsBuffer, typeBuffer;

	private void OnEnable()
	{
		proceduralGenerationController = DependencyResolver.Resolve<ProceduralGenerationController>();
		proceduralGenerationController.AddGenerator(this);
	}

	private void OnDisable()
	{
		proceduralGenerationController.RemoveGenerator(this);
	}

	int IGpuProceduralGenerator.Version { get; }

	void IGpuProceduralGenerator.Generate(RenderGraph renderGraph)
	{
		var terrain = GetComponent<Terrain>();
		if (terrain == null)
			return;

		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Terrain Tree Setup"))
		{
			var terrainData = terrain.terrainData;
			var treeCount = terrainData.treeInstanceCount;

			var prototypes = terrainData.treePrototypes;
			var gameObjects = ArrayPool<GameObject>.Get(prototypes.Length);
			for(var i = 0; i < prototypes.Length; i++)
				gameObjects[i] = prototypes[i].prefab;

			var positions = ArrayPool<Float3x4>.Get(treeCount);
			var types = ArrayPool<int>.Get(treeCount);
			var counts = new NativeArray<int>(prototypes.Length, Allocator.Temp);
			var terrainPosition = (Float3)terrain.GetPosition();
			var terrainSize = (Float3)terrainData.size;

			// Fill the buffers
			for (var i = 0; i < treeCount; i++)
			{
				var instance = terrainData.GetTreeInstance(i);
				types[i] = instance.prototypeIndex;
				counts[instance.prototypeIndex]++;

				SinCos(instance.rotation, out var sinTheta, out var cosTheta);

				var right = new Float3(cosTheta, 0, -sinTheta) * instance.widthScale;
				var up = Float3.Up * instance.heightScale;
				var fwd = new Float3(sinTheta, 0, cosTheta) * instance.widthScale;
				var position = terrainSize * instance.position + terrainPosition;
				positions[i] = new(right, up, fwd, position);
			}

			positionsBuffer = renderGraph.GetBuffer(treeCount, UnsafeUtility.SizeOf<Float3x4>());
			typeBuffer = renderGraph.GetBuffer(treeCount);
			var counterBufferHandle = renderGraph.GetBuffer(isPersistent: true);

			pass.WriteBuffer("", positionsBuffer);
			pass.WriteBuffer("", typeBuffer);
			pass.WriteBuffer("", counterBufferHandle);

			pass.SetRenderFunction((command, pass) =>
			{
				command.SetBufferData(pass.GetBuffer(positionsBuffer), positions);
				command.SetBufferData(pass.GetBuffer(typeBuffer), types);

				// TODO: Make this not neccessary.
				var index = proceduralGenerationController.AddHandleToFree(counterBufferHandle);
				var prefabStart = proceduralGenerationController.AddData(positionsBuffer, typeBuffer, gameObjects);

				proceduralGenerationController.OnRequestComplete(counts, index, prefabStart, prototypes.Length);
			});
		}
	}
}
