public struct IndirectDrawIndexedArgs
{
    public uint indexCount;
    public uint instanceCount;
    public uint startIndex;
    public uint startVertex;
    public uint startInstance;

    public IndirectDrawIndexedArgs(uint indexCount, uint instanceCount, uint startIndex, uint startVertex, uint startInstance)
    {
        this.indexCount = indexCount;
        this.instanceCount = instanceCount;
        this.startIndex = startIndex;
        this.startVertex = startVertex;
        this.startInstance = startInstance;
    }
}
