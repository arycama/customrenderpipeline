﻿using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ShadowRenderer : RenderFeature
    {
        private readonly ShadowSettings settings;
        private readonly TerrainShadowRenderer terrainShadowRenderer;

        public ShadowRenderer(ShadowSettings settings, RenderGraph renderGraph, TerrainShadowRenderer terrainShadowRenderer) : base(renderGraph)
        {
            this.settings = settings;
            this.terrainShadowRenderer = terrainShadowRenderer;
        }

        public override void Render()
        {
            var requestData = renderGraph.GetResource<ShadowRequestsData>();
            var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;
            var context = renderGraph.GetResource<RenderContextData>().Context;
            var viewData = renderGraph.GetResource<ViewData>();

            // Render Shadows
            ResourceHandle<RenderTexture> directionalShadows;
            if (requestData.DirectionalShadowRequests.Count == 0)
            {
                directionalShadows = renderGraph.EmptyTextureArray;
            }
            else
            {
                directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution, GraphicsFormat.D16_UNorm, requestData.DirectionalShadowRequests.Count, TextureDimension.Tex2DArray);

                for (var i = 0; i < requestData.DirectionalShadowRequests.Count; i++)
                {
                    var shadowRequest = requestData.DirectionalShadowRequests[i];
                    var splitData = shadowRequest.ShadowSplitData;

                    using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Directional Light Shadows"))
                    {
                        pass.Initialize(context, cullingResults, shadowRequest.VisibleLightIndex, BatchCullingProjectionType.Orthographic, splitData, settings.ShadowBias, settings.ShadowSlopeBias, false);

                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(directionalShadows);

                        pass.SetRenderFunction((
                            viewPosition: viewData.ViewPosition,
                            worldToView: shadowRequest.ViewMatrix,
                            worldToClip: shadowRequest.ProjectionMatrix * shadowRequest.ViewMatrix,
                            viewToClip: shadowRequest.ProjectionMatrix,
                            target: directionalShadows,
                            index: i
                        ),
                        (command, pass, data) =>
                        {
                            command.SetRenderTarget(pass.GetRenderTexture(data.target), 0, CubemapFace.Unknown, data.index);
                            command.ClearRenderTarget(true, false, Color.clear);

                            // TODO: Use different matrices for shadows?
                            pass.SetMatrix("_WorldToView", data.worldToView);
                            pass.SetMatrix("_WorldToClip", data.worldToClip);
                            pass.SetMatrix("_ViewToClip", data.viewToClip);
                            pass.SetVector("_ViewPosition", viewData.ViewPosition);
                        });
                    }

                    renderGraph.SetResource(new ShadowRequestData(shadowRequest, settings.ShadowBias, settings.ShadowSlopeBias, directionalShadows, i));
                    terrainShadowRenderer.Render();
                }
            }

            ListPool<ShadowRequest>.Release(requestData.DirectionalShadowRequests);

            // Process point shadows 
            ResourceHandle<RenderTexture> pointShadows;
            if (requestData.PointShadowRequests.Count == 0)
            {
                pointShadows = renderGraph.EmptyCubemapArray;
            }
            else
            {
                pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D32_SFloat, requestData.PointShadowRequests.Count, TextureDimension.CubeArray);

                for (var i = 0; i < requestData.PointShadowRequests.Count; i++)
                {
                    var shadowRequest = requestData.PointShadowRequests[i];
                    if (!shadowRequest.IsValid)
                        continue;

                    using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Point Light Shadows"))
                    {
                        pass.Initialize(context, cullingResults, shadowRequest.VisibleLightIndex, BatchCullingProjectionType.Perspective, shadowRequest.ShadowSplitData, settings.PointShadowBias, settings.PointShadowSlopeBias, true);

                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(pointShadows);

                        pass.SetRenderFunction((
                            viewPosition: viewData.ViewPosition,
                            worldToView: shadowRequest.ViewMatrix,
                            worldToClip: GL.GetGPUProjectionMatrix(shadowRequest.ProjectionMatrix, true) * shadowRequest.ViewMatrix,
                            target: pointShadows,
                            index: i
                        ),
                        (command, pass, data) =>
                        {
                            command.SetRenderTarget(pass.GetRenderTexture(data.target), pass.GetRenderTexture(data.target), 0, CubemapFace.Unknown, data.index);
                            command.ClearRenderTarget(true, false, Color.clear);

                            pass.SetVector("_ViewPosition", viewData.ViewPosition);
                            pass.SetMatrix("_WorldToView", data.worldToView);
                            pass.SetMatrix("_WorldToClip", data.worldToClip);
                        });

                    }
                }
            }

            ListPool<ShadowRequest>.Release(requestData.PointShadowRequests);

            var result = new Result(directionalShadows, pointShadows, settings.DirectionalShadowResolution, 1.0f / settings.DirectionalShadowResolution, settings.PcfFilterRadius, settings.PcfFilterSigma);
            renderGraph.SetResource(result); ;
        }

        public readonly struct Result : IRenderPassData
        {
            private readonly ResourceHandle<RenderTexture> directionalShadows;
            private readonly ResourceHandle<RenderTexture> pointShadows;
            private readonly int shadowMapResolution;
            private readonly float rcpShadowMapResolution;
            private readonly float shadowFilterRadius;
            private readonly float shadowFilterSigma;

            public Result(ResourceHandle<RenderTexture> directionalShadows, ResourceHandle<RenderTexture> pointShadows, int shadowMapResolution, float rcpShadowMapResolution, float shadowFilterRadius, float shadowFilterSigma)
            {
                this.directionalShadows = directionalShadows;
                this.pointShadows = pointShadows;
                this.shadowMapResolution = shadowMapResolution;
                this.rcpShadowMapResolution = rcpShadowMapResolution;
                this.shadowFilterRadius = shadowFilterRadius;
                this.shadowFilterSigma = shadowFilterSigma;
            }

            public readonly void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_DirectionalShadows", directionalShadows);
                pass.ReadTexture("_PointShadows", pointShadows);
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetFloat("ShadowMapResolution", shadowMapResolution);
                pass.SetFloat("RcpShadowMapResolution", rcpShadowMapResolution);
                pass.SetFloat("ShadowFilterRadius", shadowFilterRadius);
                pass.SetFloat("ShadowFilterSigma", shadowFilterSigma);
            }
        }
    }
}