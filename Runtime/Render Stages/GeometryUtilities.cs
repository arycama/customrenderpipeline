using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public static class GeometryUtilities
    {
        // Solves the quadratic equation of the form: a*t^2 + b*t + c = 0.
        // Returns 'false' if there are no real roots, 'true' otherwise.
        public static bool SolveQuadraticEquation(float a, float b, float c, out Vector2 roots)
        {
            var discriminant = b * b - 4f * a * c;
            var sqrtDet = Mathf.Sqrt(discriminant);

            roots.x = (-b - sqrtDet) / (2f * a);
            roots.y = (-b + sqrtDet) / (2f * a);

            return discriminant >= 0f;
        }

        // This simplified version assume that we care about the result only when we are inside the sphere
        // Assume Sphere is at the origin (i.e start = position - spherePosition) and dir is normalized
        // Ref: http://http.developer.nvidia.com/GPUGems/gpugems_ch19.html
        public static float IntersectRaySphereSimple(Vector3 start, Vector3 dir, float radius)
        {
            var b = Vector3.Dot(dir, start) * 2.0f;
            var c = Vector3.Dot(start, start) - radius * radius;
            var discriminant = b * b - 4.0f * c;

            return Mathf.Abs(Mathf.Sqrt(discriminant) - b) * 0.5f;
        }

        // Assume Sphere is at the origin (i.e start = position - spherePosition)
        public static bool IntersectRaySphere(Vector3 start, Vector3 dir, float radius, out Vector2 intersections)
        {
            var a = Vector3.Dot(dir, dir);
            var b = Vector3.Dot(dir, start) * 2.0f;
            var c = Vector3.Dot(start, start) - radius * radius;

            return SolveQuadraticEquation(a, b, c, out intersections);
        }

        public static float IntersectRayPlane(Vector3 rayOrigin, Vector3 rayDirection, Vector3 planeOrigin, Vector3 planeNormal, out bool isValid)
        {
            var denom = Vector3.Dot(planeNormal, rayDirection);
            isValid = Mathf.Abs(denom) > 1e-6f;
            return Vector3.Dot(planeNormal, planeOrigin - rayOrigin) / denom;
        }

        public static float IntersectRayPlane(Vector3 rayOrigin, Vector3 rayDirection, Vector3 planeOrigin, Vector3 planeNormal)
        {
            return IntersectRayPlane(rayOrigin, rayDirection, planeOrigin, planeNormal, out _);
        }
    }
}