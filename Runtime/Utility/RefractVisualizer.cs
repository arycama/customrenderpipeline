using UnityEngine;

public class RefractVisualizer : MonoBehaviour
{
    public Float3 lightRotation;
    public Float3 viewRotation;
    public Float3 halfRotation;
    [Range(0, 2)] public float ni = 1.0f;
    [Range(0, 2)] public float no = 1.5f;
    public bool showTest = false;

    public float NdotVp;
    public float LdotVp;
    public float VdotVp;

    public float NdotL;
    public float NdotV;
    public float LdotV;

    public float hLenSq0;
    public float hLenSq1;

    public float VdotH;

    public float NdotH0;
    public float NdotH1;

    public float VdotH0;
    public float VdotH1;

    public float LdotH0;
    public float LdotH1;

    private void OnDrawGizmos()
	{
        var eta = ni <= no ? no / ni : ni / no;

        var N = Float3.Up;
        var V = Quaternion.Euler(viewRotation).Forward;
        var L = Quaternion.Euler(lightRotation).Forward;
        var Htest = Quaternion.Euler(halfRotation).Forward;
        var vp = Float3.Refract(-V, N, 1 / eta);

        NdotL = Float3.Dot(N, L);
        NdotV = Float3.Dot(N, V);
        LdotV = Float3.Dot(L, V);

        Gizmos.color = Color.coral;
        Gizmos.DrawLine(Float3.Zero, vp);
        NdotVp = Float3.Dot(N, vp);
        LdotVp = Float3.Dot(L, vp);
        VdotVp = Float3.Dot(V, vp);

        hLenSq0 = Float3.SquareMagnitude(L + V * eta);
        var H = -Float3.Normalize(L + V * eta);
        VdotH = Float3.Dot(V, H);

        //if (VdotH > 0.0)
        //{
        //    H = -H;
        //    VdotH = -VdotH;
        //}

        var Rp = Float3.Refract(-L, H, 1.0f / eta);

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(Float3.Zero, N);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(Float3.Zero, L);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(Float3.Zero, V * 2);

        // Refracted half vector
        Gizmos.color = Color.white;
        Gizmos.DrawLine(Float3.Zero, H);

        // Calcualted refracted ray
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(Float3.Zero, Rp);

        if (showTest)
        {
            Gizmos.color = Color.orange;
            Gizmos.DrawLine(Float3.Zero, Htest);

            Gizmos.color = Color.darkGray;
            Gizmos.DrawLine(Float3.Zero, Float3.Refract(-L, Htest, 1 / eta));
        }

        // Save some valeus for comparison
        NdotH0 = Float3.Dot(N, H);
        LdotH0 = Float3.Dot(L, H);
        VdotH0 = Float3.Dot(V, H);

        hLenSq1 = 1 + 2 * eta * LdotV + eta * eta;

        var denom = Math.Sqrt(hLenSq1);
        NdotH1 = (-eta * NdotV - NdotL) / denom;
        VdotH1 = (-LdotV - eta) / denom;
        LdotH1 = (-eta * LdotV - 1) / denom;
    }
}