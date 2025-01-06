﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    [ExecuteAlways, RequireComponent(typeof(Light))]
    public class CelestialBody : MonoBehaviour
    {
        private static readonly List<CelestialBody> celestialBodies = new();
        public static List<CelestialBody> CelestialBodies => celestialBodies;

        [SerializeField, Range(0, 90)] private float angularDiameter = 0.53f;
        [SerializeField] private Mesh mesh = null;
        [SerializeField] private Material material = null;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private float intensity = Mathf.PI;

        public float AngularDiameter => angularDiameter;
        public Color Color => color.linear * intensity;
        public Vector3 Direction => -transform.forward;

        private void OnEnable()
        {
            celestialBodies.Add(this);
        }

        private void OnDisable()
        {
            _ = celestialBodies.Remove(this);
        }

        public void Render(CommandBuffer command, Vector3 viewPosition)
        {
            if (mesh == null || material == null)
                return;

            var colors = ListPool<Vector4>.Get();
            var directions = ListPool<Vector4>.Get();

            foreach (var body in celestialBodies)
            {
                if (body == this)
                    continue;

                colors.Add(body.Color);
                directions.Add(body.Direction);
            }

            var propertyBlock = GenericPool<MaterialPropertyBlock>.Get();
            propertyBlock.Clear();
            propertyBlock.SetFloat("_AngularDiameter", angularDiameter);
            propertyBlock.SetInt("_CelestialBodyCount", celestialBodies.Count);
            propertyBlock.SetVector("_Luminance", Color);
            propertyBlock.SetVector("_Direction", Direction);

            if (colors.Count > 0)
            {
                propertyBlock.SetVectorArray("_CelestialBodyColors", colors);
                propertyBlock.SetVectorArray("_CelestialBodyDirections", directions);
            }

            var scale = 2 * Mathf.Tan(0.5f * angularDiameter * Mathf.Deg2Rad);
            var matrix = Matrix4x4.TRS(viewPosition - transform.forward, Quaternion.LookRotation(-transform.forward), Vector3.one * scale);

            command.DrawMesh(mesh, matrix, material, 0, 0, propertyBlock);
            GenericPool<MaterialPropertyBlock>.Release(propertyBlock);
            ListPool<Vector4>.Release(colors);
            ListPool<Vector4>.Release(directions);
        }
    }
}