using UnityEngine;

public class RefractVisualizer : MonoBehaviour
{
    public Float3 lightRotation;
    public Float3 viewRotation;
    public Float3 halfRotation;
    [Range(0, 2)] public float ni = 1.0f;
    [Range(0, 2)] public float no = 1.5f;
    public bool showTest = false;

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
        var N = Float3.Up;
        var L = Quaternion.Euler(lightRotation).Forward;
        var V = Quaternion.Euler(viewRotation).Forward;
        var Htest = Quaternion.Euler(halfRotation).Forward;

        NdotL = Float3.Dot(N, L);
        NdotV = Float3.Dot(N, V);
        LdotV = Float3.Dot(L, V);

        var eta = no / ni;

        hLenSq0 = Float3.SquareMagnitude(L + V * eta);
        var H = Float3.Normalize(ni * L + no * V);

        if (no > ni)
            H = -H;

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
        NdotH1 = (NdotL + NdotV * eta) / denom;
        VdotH1 = (LdotV + eta) / denom;
        LdotH1 = (1 + eta * LdotV) / denom;

        if(no > ni)
        {
            NdotH1 = -NdotH1;
            VdotH1 = -VdotH1;
            LdotH1 = -LdotH1;
        }
    }
}