using UnityEngine;

public static class BoundsExtensions
{
    /// <summary> Transforms a bounds by a matrix </summary>
    public static Bounds Transform(this Bounds bounds, Matrix4x4 matrix)
    {
        var result = new Bounds();
        for (var j = 0; j < 8; j++)
        {
            var x = j & 1;
            var y = (j >> 1) & 1;
            var z = j >> 2;

            var position = bounds.Min + Vector3.Scale(bounds.Size, new Vector3(x, y, z));
            var matrixPosition = matrix.MultiplyPoint3x4(position);

            if (j == 0)
            {
                result = new Bounds(matrixPosition, Vector3.zero);
            }
            else
            {
                result.Encapsulate(matrixPosition);
            }
        }

        return result;
    }
}