using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class WaterCaustics : RenderFeature
    {
        private WaterSystem.Settings settings;
        private Material material;
        private Mesh causticsMesh;

        public WaterCaustics(RenderGraph renderGraph, WaterSystem.Settings settings) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Water Caustics")) { hideFlags = HideFlags.HideAndDontSave };

            causticsMesh = GeneratePlane(new Vector3(0.5f, 0, 0.5f), 1, 128);
        }

        public static Mesh GeneratePlane(Vector3 origin, float size, int divisions)
        {
            var interval = size / divisions;
            var offset = origin;// size / 2f;

            var vertices = new Vector3[(divisions + 1) * (divisions + 1)];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];

            for (int i = 0, z = 0; z <= divisions; z++)
            {
                for (int x = 0; x <= divisions; x++, i++)
                {
                    vertices[i] = new Vector3(x * interval - offset.x, 0, z * interval - offset.z);
                    uvs[i] = new Vector2(x / (float)(divisions), z / (float)(divisions));
                    normals[i] = new Vector3(0, 1, 0);
                    tangents[i] = new Vector4(1, 0, 0, -1);
                }
            }

            var triangles = new int[divisions * divisions * 6];
            for (int ti = 0, vi = 0, y = 0; y < divisions; y++, vi++)
            {
                for (int x = 0; x < divisions; x++, ti += 6, vi++)
                {
                    triangles[ti] = vi;
                    triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                    triangles[ti + 4] = triangles[ti + 1] = vi + divisions + 1;
                    triangles[ti + 5] = vi + divisions + 2;
                }
            }

            var halfSize = size / 2f;
            var center = new Vector3(origin.x - halfSize, 0, origin.z - halfSize);
            var sizeVector = new Vector3(size, 0, size);
            var bounds = new Bounds(center, sizeVector);

            var mesh = new Mesh()
            {
                name = "Plane",
                indexFormat = vertices.Length < ushort.MaxValue ? IndexFormat.UInt16 : IndexFormat.UInt32,
                vertices = vertices,
                normals = normals,
                tangents = tangents,
                uv = uvs,
                bounds = bounds
            };

            mesh.SetTriangles(triangles, 0, false);

            return mesh;
        }

        public void Render()
        {
            var Profile = settings.Profile;
            var patchSizes = new Vector4(Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 0f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 1f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 2f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 3f));

            var patchSize = patchSizes[settings.CasuticsCascade];
            var tempResult = renderGraph.GetTexture(settings.CasuticsResolution * 2, settings.CasuticsResolution * 2, GraphicsFormat.R16G16B16A16_SFloat);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Caustics Render"))
            {
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<OceanFftResult>();
                pass.WriteTexture(tempResult);

                pass.SetRenderFunction((command, pass) =>
                {
                    var viewMatrix = Matrix4x4.LookAt(Vector3.zero, Vector3.down, Vector3.forward).inverse;
                    var projectionMatrix = Matrix4x4.Ortho(-patchSize, patchSize, -patchSize, patchSize, 0, settings.CausticsDepth * 2);
                    command.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

                    command.SetRenderTarget(tempResult);
                    command.ClearRenderTarget(false, true, Color.clear);

                    pass.SetFloat(command, "_CausticsDepth", settings.CausticsDepth);
                    pass.SetFloat(command, "_CausticsCascade", settings.CasuticsCascade);
                    pass.SetFloat(command, "_CausticsSpacing", patchSize / 128f);
                    pass.SetVector(command, "_RefractiveIndex", Vector3.one * (1.0f / 1.34f));

                    command.DrawMesh(causticsMesh, Matrix4x4.Scale(new Vector3(patchSize, patchSize, patchSize)), material, 0, 0);
                });
            }

            var result = renderGraph.GetTexture(settings.CasuticsResolution, settings.CasuticsResolution, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true, autoGenerateMips: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Caustics Blit"))
            {
                pass.Initialize(material, 1);
                pass.ReadTexture("_MainTex", tempResult);
                pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
            }

            renderGraph.ResourceMap.SetRenderPassData(new CausticsResult(result, settings.CasuticsCascade, settings.CausticsDepth), renderGraph.FrameIndex);
        }
    }
}