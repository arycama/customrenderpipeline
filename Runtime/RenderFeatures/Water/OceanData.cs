public readonly struct OceanData
{
    private readonly float windSpeed, windAngle, fetch, spreadBlend, swell, peakEnhancement, shortWavesFade;

    public OceanData(float windSpeed, float windAngle, float fetch, float spreadBlend, float swell, float peakEnhancement, float shortWavesFade)
    {
        this.windSpeed = windSpeed;
        this.windAngle = windAngle;
        this.fetch = fetch;
        this.spreadBlend = spreadBlend;
        this.swell = swell;
        this.peakEnhancement = peakEnhancement;
        this.shortWavesFade = shortWavesFade;
    }
}
