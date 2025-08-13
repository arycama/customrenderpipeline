public readonly struct TerrainLayerData
{
	private readonly float scale;
	private readonly float blending;
	private readonly float stochastic;
	private readonly float rotation;

	public TerrainLayerData(float scale, float blending, float stochastic, float rotation)
	{
		this.scale = scale;
		this.blending = blending;
		this.stochastic = stochastic;
		this.rotation = rotation;
	}
}