using System;
using UnityEngine;

[Serializable]
public class LensSettings
{
	[SerializeField, Min(1.0f)] private float aperture = 11.0f;
	[SerializeField] private float shutterSpeed = 200.0f;
	[SerializeField] private float iso = 200f;
	[SerializeField] private float sensorSize = 24.89f;
	[SerializeField] private float focalDistance = 15f;

	public float Aperture => aperture;
	public float ShutterSpeed => 1f / shutterSpeed;
	public float Iso => iso;
	public float SensorSize => sensorSize;
	public float FocalDistance => focalDistance;

	public float GetFocalLength(float fov) => sensorSize / (2.0f * Mathf.Tan(fov * Mathf.Deg2Rad / 2.0f));
}
