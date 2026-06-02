using Unmath;

public readonly struct ViewParameter
{
    public readonly Quaternion rotation;
    public readonly Float3 position;

    public readonly Float2 tanHalfFov; // TODO: I think this might be equivalent across both eyes
    public readonly Float2 offset;

    public ViewParameter(Float4x4 worldToView, Float4x4 viewToClip)
    {
        rotation = new Quaternion(worldToView.r0.xyz, worldToView.r1.xyz, -worldToView.r2.xyz);
        position = -new Float3(worldToView.c0.xyz.Dot(worldToView.c3.xyz), worldToView.c1.xyz.Dot(worldToView.c3.xyz), worldToView.c2.xyz.Dot(worldToView.c3.xyz));
        tanHalfFov = new Float2(1.0f / viewToClip.m00, 1.0f / viewToClip.m11);
        offset = -new Float2(viewToClip.m02, viewToClip.m12);
    }
}