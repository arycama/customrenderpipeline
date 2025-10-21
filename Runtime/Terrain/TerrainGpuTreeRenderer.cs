using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static Math;

[ExecuteAlways]
public class TerrainGpuTreeRenderer : MonoBehaviour, IGpuProceduralGenerator
{
	private ProceduralGenerationController proceduralGenerationController;

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

		var terrainData = terrain.terrainData;
		var treeCount = terrainData.treeInstanceCount;

		var prototypes = terrainData.treePrototypes;
		var gameObjects = ArrayPool<GameObject>.Get(prototypes.Length);
		for (var i = 0; i < prototypes.Length; i++)
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

		Array.Sort(types, positions);
		Array.Sort(types);

		var positionsBuffer = renderGraph.GetBuffer(treeCount, UnsafeUtility.SizeOf<Float3x4>(), isPersistent: true);
		var typeBuffer = renderGraph.GetBuffer(treeCount, isPersistent: true);
		var counterBufferHandle = renderGraph.GetBuffer(isPersistent: true);
		var prefabCount = prototypes.Length;

		using (var pass = renderGraph.AddGenericRenderPass("Terrain Tree Setup", (positionsBuffer, typeBuffer, positions, types, proceduralGenerationController, counterBufferHandle, gameObjects, counts, prefabCount)))
		{
			pass.WriteBuffer("", positionsBuffer);
			pass.WriteBuffer("", typeBuffer);
			pass.WriteBuffer("", counterBufferHandle);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.SetBufferData(pass.GetBuffer(data.positionsBuffer), data.positions);
				command.SetBufferData(pass.GetBuffer(data.typeBuffer), data.types);

				// TODO: Make this not neccessary.
				var index = data.proceduralGenerationController.AddHandleToFree(data.counterBufferHandle);
				var prefabStart = data.proceduralGenerationController.AddData(data.positionsBuffer, data.typeBuffer, data.gameObjects);

				data.proceduralGenerationController.OnRequestComplete(data.counts, index, prefabStart, data.prefabCount);
			});
		}
	}
}
