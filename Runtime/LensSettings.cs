using System;
using UnityEngine;

[Serializable]
public class LensSettings
{
    [SerializeField] private float aperture = 16f;
    [SerializeField] private float shutterSpeed = 0.005f;
    [SerializeField] private float iso = 200f;
    //[SerializeField] private float sensorWidth = 24.89f;
    [SerializeField] private float sensorHeight = 24.89f;
    [SerializeField] private float focalDistance = 15f;

    public float Aperture => aperture;
    public float ShutterSpeed => shutterSpeed;
    public float Iso => iso;
    public float SensorHeight => sensorHeight;
    public float FocalDistance => focalDistance;

    public float GetFocalLength(float fov) => sensorHeight / (2.0f * Mathf.Tan(fov * Mathf.Deg2Rad / 2.0f));
}