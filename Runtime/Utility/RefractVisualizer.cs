using Mono.Cecil.Cil;
using UnityEngine;

public class RefractVisualizer : MonoBehaviour
{
    public Float3 lightRotation;
    public Float3 viewRotation;
    public Float3 halfRotation;
    [Range(0, 2)] public float ni = 1.0f;
    [Range(0, 2)] public float no = 1.5f;

    public bool incoming, outgoing, incomingRefract, outgoingRefract;

    public float NdotL, NdotV, NdotH, LdotV, LdotH, VdotH, NdotLt, NdotVt, NdotHt, LdotVt, LdotHt, VdotHt;

    private void OnDrawGizmos()
    {
        // Top layer
        var N = Float3.Up;
        var V = Quaternion.Euler(viewRotation).Forward;
        var L = Float3.Refract(-V, N, ni, no); // Incomign direction is refracted view vector
        var H = Float3.Normalize(L + V * ni / no);

        if (ni > no)
            H = -H;

        Gizmos.matrix = transform.localToWorldMatrix;

        if (outgoing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Float3.Zero, N);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(Float3.Zero, L);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(Float3.Zero, V);
            Gizmos.color = Color.white;
            Gizmos.DrawLine(Float3.Zero, H);
        }

        if(outgoingRefract)
        {
            // Ni and no refer to reractive indices wrt the normal, eg ni = refractive index the normal poitns to, no is the backfacing ior
            var T = Float3.Refract(-L, H, no, ni);
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(Float3.Zero, T);
        }

        NdotL = Float3.Dot(N, L);
        NdotV = Float3.Dot(N, V);
        NdotH = Float3.Dot(N, H);
        LdotV = Float3.Dot(L, V);
        LdotH = Float3.Dot(L, H);
        VdotH = Float3.Dot(V, H);

        // Bottom layer
        var Nt = -N;
        var Vt = -L; // Outgoing direction is refracted view vectr
        var Lt = Quaternion.Euler(lightRotation).Forward;
        var Ht = Float3.Normalize(Lt * ni + Vt * no);

        if (no > ni)
            Ht = -Ht;

        NdotLt = Float3.Dot(Nt, Lt);
        NdotVt = Float3.Dot(Nt, Vt);
        NdotHt = Float3.Dot(Nt, Ht);
        LdotVt = Float3.Dot(Lt, Vt);
        LdotHt = Float3.Dot(Lt, Ht);
        VdotHt = Float3.Dot(Vt, Ht);

        if (incoming)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Float3.Zero, Nt);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(Float3.Zero, Lt);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(Float3.Zero, Vt);
            Gizmos.color = Color.white;
            Gizmos.DrawLine(Float3.Zero, Ht);
        }

        if (incomingRefract)
        {
            var T = Float3.Refract(-Lt, Ht, ni, no);
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(Float3.Zero, T);
        }
    }
}