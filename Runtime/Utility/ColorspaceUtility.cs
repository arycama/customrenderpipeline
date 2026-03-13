using UnityEngine;

public static class ColorspaceUtility
{
    public static Float3 Rec709ToRec2020(Float3 x) => new
    (
        x.x * 0.627402f + x.y * 0.329292f + x.z * 0.043306f,
        x.x * 0.069095f + x.y * 0.919544f + x.z * 0.011360f,
        x.x * 0.016394f + x.y * 0.088013f + x.z * 0.895593f
    );

    public static Float3 XyzToRec2020(Float3 x) => new
    (
        x.x * 1.716651f + x.y * -0.355671f + x.z * -0.253366f,
        x.x * -0.666684f + x.y * 1.616481f + x.z * 0.015769f,
        x.x * 0.017640f + x.y * -0.042771f + x.z * 0.942103f
    );

    public static Float3 XyzToRec709(Float3 x) => new
    (
        x.x * 3.240970f + x.y * -1.537383f + x.z * -0.498611f,
        x.x * -0.969244f + x.y * 1.875968f + x.z * 0.041555f,
        x.x * 0.055630f + x.y * -0.203977f + x.z * 1.056972f
    );
}
