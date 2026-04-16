using System;
using UnityEngine;

[Serializable]
public class LensSettings
{
	[SerializeField, Min(1.0f)] private float aperture = 11.0f;
	[SerializeField] private float shutterSpeed = 200.0f;
	[SerializeField] private float iso = 200f;
	[SerializeField] private float sensorSize = 24.89f;

	public float Aperture => aperture;
	public float ShutterSpeed => 1f / shutterSpeed;
	public float Iso => iso;
	public float SensorSize => sensorSize;
}
