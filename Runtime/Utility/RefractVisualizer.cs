using UnityEngine;

public class RefractVisualizer : MonoBehaviour
{
	public Float3 incidentDirection = new Float3(Math.Sqrt(0.5f), Math.Sqrt(0.5f), 0);
	[Range(0, 2)] public float ni = 1.0f;
    [Range(0, 2)] public float no = 1.5f;

	private void OnDrawGizmos()
	{
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(Float3.Zero, Float3.Up);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(Float3.Zero, incidentDirection.Normalized);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(Float3.Zero, Float3.Refract(-incidentDirection.Normalized, Float3.Up, ni / no));
	}
}