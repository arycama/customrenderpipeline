using Unmath;

public static class BoundsExtensions
{
    /// <summary> Transforms a bounds by a matrix </summary>
    public static Bounds Transform(this Bounds bounds, Float4x4 matrix)
    {
        var result = new Bounds();
        for (var j = 0; j < 8; j++)
        {
            var x = j & 1;
            var y = (j >> 1) & 1;
            var z = j >> 2;

            var position = bounds.Min + (bounds.Size * new Float3(x, y, z));
            var matrixPosition = matrix.MultiplyPoint3x4(position);

            if (j == 0)
            {
                result = new Bounds(matrixPosition, 0);
            }
            else
            {
				_ = result.Encapsulate(matrixPosition);
            }
        }

        return result;
    }
}