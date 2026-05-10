using UnityEngine;

public static class RectIntExtensions
{
	public static RectInt Encapsulate(this RectInt rectInt, int x, int y)
	{
        // TODO: Why is there a +1
        rectInt.xMin = Math.Min(x, rectInt.xMin);
		rectInt.xMax = Math.Max(x + 1, rectInt.xMax);
		rectInt.yMin = Math.Min(y, rectInt.yMin);
		rectInt.yMax = Math.Max(y + 1, rectInt.yMax);
		return rectInt;
	}

	public static RectInt Encapsulate(this RectInt rectInt, Vector2Int position)
	{
        // TODO: Why is there a +1
		rectInt.xMin = Math.Min(position.x, rectInt.xMin);
		rectInt.xMax = Math.Max(position.x + 1, rectInt.xMax);
		rectInt.yMin = Math.Min(position.y, rectInt.yMin);
		rectInt.yMax = Math.Max(position.y + 1, rectInt.yMax);
		return rectInt;
	}

    public static RectInt Encapsulate(this RectInt a, RectInt b)
    {
        a.xMin = Math.Min(a.xMin, b.xMin);
        a.xMax = Math.Max(a.xMax, b.xMax);
        a.yMin = Math.Min(a.yMin, b.yMin);
        a.yMax = Math.Max(a.yMax, b.yMax);
        return a;
    }
}