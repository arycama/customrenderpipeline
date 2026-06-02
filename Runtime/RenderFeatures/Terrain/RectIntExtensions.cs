using UnityEngine;
using static Unmath.Math;

public static class RectIntExtensions
{
	public static RectInt Encapsulate(this RectInt rectInt, int x, int y)
	{
        // TODO: Why is there a +1
        rectInt.xMin = Min(x, rectInt.xMin);
		rectInt.xMax = Max(x + 1, rectInt.xMax);
		rectInt.yMin = Min(y, rectInt.yMin);
		rectInt.yMax = Max(y + 1, rectInt.yMax);
		return rectInt;
	}

	public static RectInt Encapsulate(this RectInt rectInt, Vector2Int position)
	{
        // TODO: Why is there a +1
		rectInt.xMin = Min(position.x, rectInt.xMin);
		rectInt.xMax = Max(position.x + 1, rectInt.xMax);
		rectInt.yMin = Min(position.y, rectInt.yMin);
		rectInt.yMax = Max(position.y + 1, rectInt.yMax);
		return rectInt;
	}

    public static RectInt Encapsulate(this RectInt a, RectInt b)
    {
        a.xMin = Min(a.xMin, b.xMin);
        a.xMax = Max(a.xMax, b.xMax);
        a.yMin = Min(a.yMin, b.yMin);
        a.yMax = Max(a.yMax, b.yMax);
        return a;
    }
}