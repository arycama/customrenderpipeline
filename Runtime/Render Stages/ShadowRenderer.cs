using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ShadowRenderer : RenderFeature
    {
        private readonly ShadowSettings settings;
        private readonly TerrainSystem terrainSystem;

        public ShadowRenderer(ShadowSettings settings, RenderGraph renderGraph, TerrainSystem terrainSystem) : base(renderGraph)
        {
            this.settings = settings;
            this.terrainSystem = terrainSystem;
        }

        public void Render(ScriptableRenderContext context, CullingResults cullingResults, Camera camera, List<ShadowRequest> directionalShadowRequests, List<ShadowRequest> pointShadowRequests, Vector3 viewPosition, ICommonPassData commonPassData)
        {
            // Render Shadows
            RTHandle directionalShadows;
            if (directionalShadowRequests.Count == 0)
            {
                directionalShadows = renderGraph.EmptyTextureArray;
            }
            else
            {
                directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution, GraphicsFormat.D16_UNorm, directionalShadowRequests.Count, TextureDimension.Tex2DArray);

                for (var i = 0; i < directionalShadowRequests.Count; i++)
                {
                    var shadowRequest = directionalShadowRequests[i];
                    var splitData = shadowRequest.ShadowSplitData;

                    using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Directional Light Shadows"))
                    {
                        pass.Initialize(context, cullingResults, shadowRequest.VisibleLightIndex, BatchCullingProjectionType.Orthographic, splitData, settings.ShadowBias, settings.ShadowSlopeBias, false);

                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(directionalShadows);

                        var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                        {
                            command.SetRenderTarget(data.target, 0, CubemapFace.Unknown, data.index);
                            command.ClearRenderTarget(true, false, Color.clear);

                            pass.SetMatrix(command, "_WorldToView", data.worldToView);
                            pass.SetMatrix(command, "_WorldToClip", data.worldToClip);
                            pass.SetVector(command, "_ViewPosition", data.viewPosition);
                        });

                        data.viewPosition = camera.transform.position;
                        data.worldToView = shadowRequest.ViewMatrix;
                        data.worldToClip = shadowRequest.ProjectionMatrix * shadowRequest.ViewMatrix;
                        data.target = directionalShadows;
                        data.index = i;
                    }

                    terrainSystem.CullShadow(viewPosition, shadowRequest.CullingPlanes, commonPassData);
                    terrainSystem.RenderShadow(viewPosition, directionalShadows, shadowRequest.CullingPlanes, commonPassData, shadowRequest.ProjectionMatrix * shadowRequest.ViewMatrix, i, settings.ShadowBias, settings.ShadowSlopeBias);
                }
            }

            ListPool<ShadowRequest>.Release(directionalShadowRequests);

            // Process point shadows 
            RTHandle pointShadows;
            if (pointShadowRequests.Count == 0)
            {
                pointShadows = renderGraph.EmptyCubemapArray;
            }
            else
            {
                pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D32_SFloat, pointShadowRequests.Count, TextureDimension.CubeArray);

                for (var i = 0; i < pointShadowRequests.Count; i++)
                {
                    var shadowRequest = pointShadowRequests[i];
                    if (!shadowRequest.IsValid)
                        continue;

                    using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Point Light Shadows"))
                    {
                        pass.Initialize(context, cullingResults, shadowRequest.VisibleLightIndex, BatchCullingProjectionType.Perspective, shadowRequest.ShadowSplitData, settings.PointShadowBias, settings.PointShadowSlopeBias, true);

                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(pointShadows);

                        var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                        {
                            command.SetRenderTarget(data.target, data.target, 0, CubemapFace.Unknown, data.index);
                            command.ClearRenderTarget(true, false, Color.clear);

                            pass.SetVector(command, "_ViewPosition", data.viewPosition);
                            pass.SetMatrix(command, "_WorldToView", data.worldToView);
                            pass.SetMatrix(command, "_WorldToClip", data.worldToClip);
                        });

                        data.viewPosition = camera.transform.position;
                        data.worldToView = shadowRequest.ViewMatrix;
                        data.worldToClip = GL.GetGPUProjectionMatrix(shadowRequest.ProjectionMatrix, true) * shadowRequest.ViewMatrix;
                        data.target = pointShadows;
                        data.index = i;
                    }
                }
            }

            ListPool<ShadowRequest>.Release(pointShadowRequests);

            var result = new Result(directionalShadows, pointShadows, settings.DirectionalShadowResolution, 1.0f / settings.DirectionalShadowResolution, settings.PcfFilterRadius, settings.PcfFilterSigma);
            renderGraph.ResourceMap.SetRenderPassData(result, renderGraph.FrameIndex);
        }

        private class PassData
        {
            internal Vector3 viewPosition;
            internal Matrix4x4 worldToView;
            internal Matrix4x4 worldToClip;
            internal RTHandle target;
            internal int index;
        }

        public struct Result : IRenderPassData
        {
            private readonly RTHandle directionalShadows;
            private readonly RTHandle pointShadows;
            private readonly int shadowMapResolution;
            private readonly float rcpShadowMapResolution;
            private readonly float shadowFilterRadius;
            private readonly float shadowFilterSigma;

            public Result(RTHandle directionalShadows, RTHandle pointShadows, int shadowMapResolution, float rcpShadowMapResolution, float shadowFilterRadius, float shadowFilterSigma)
            {
                this.directionalShadows = directionalShadows ?? throw new ArgumentNullException(nameof(directionalShadows));
                this.pointShadows = pointShadows ?? throw new ArgumentNullException(nameof(pointShadows));
                this.shadowMapResolution = shadowMapResolution;
                this.rcpShadowMapResolution = rcpShadowMapResolution;
                this.shadowFilterRadius = shadowFilterRadius;
                this.shadowFilterSigma = shadowFilterSigma;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_DirectionalShadows", directionalShadows);
                pass.ReadTexture("_PointShadows", pointShadows);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetFloat(command, "ShadowMapResolution", shadowMapResolution);
                pass.SetFloat(command, "RcpShadowMapResolution", rcpShadowMapResolution);
                pass.SetFloat(command, "ShadowFilterRadius", shadowFilterRadius);
                pass.SetFloat(command, "ShadowFilterSigma", shadowFilterSigma);
            }
        }
    }
}