using UnityEngine;

public readonly struct DisplayData
{
    public readonly ColorGamut colorGamut;
    public readonly float peakLuminance;
    public readonly bool hdrAvailable;

    public DisplayData(ColorGamut colorGamut, float peakLuminance, bool hdrAvailable)
    {
        this.colorGamut = colorGamut;
        this.peakLuminance = peakLuminance;
        this.hdrAvailable = hdrAvailable;
    }
}