public struct IndirectDrawArgs
{
	public uint vertexCount;
	public uint instanceCount;
	public uint startVertex;
	public uint startInstance;

	public IndirectDrawArgs(uint vertexCount, uint instanceCount, uint startVertex, uint startInstance)
	{
		this.vertexCount = vertexCount;
		this.instanceCount = instanceCount;
		this.startVertex = startVertex;
		this.startInstance = startInstance;
	}
}