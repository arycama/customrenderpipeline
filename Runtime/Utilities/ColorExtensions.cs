using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ColorExtensions
{
    public static Vector3 AsVector3(this Color color)
    {
        return new Vector3(color.r, color.g, color.b);
    }
}
