using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field)]
public class GammaAttribute : PropertyAttribute
{
	public float Gamma { get; }

	public GammaAttribute(float gamma)
	{
		Gamma = gamma;
	}
}
