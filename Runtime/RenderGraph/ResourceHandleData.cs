public struct ResourceHandleData<V, T> where V : IResourceDescriptor<T>
{
	public int createIndex;
	public int freeIndex;
	public int resourceIndex;
	public V descriptor;
	public bool isAssigned;
	public bool isReleasable;
	public bool isPersistent;
	public bool isUsed;

	public ResourceHandleData(int createIndex, int freeIndex, int resourceIndex, V descriptor, bool isAssigned, bool isReleasable, bool isPersistent, bool isUsed)
	{
		this.createIndex = createIndex;
		this.freeIndex = freeIndex;
		this.resourceIndex = resourceIndex;
		this.descriptor = descriptor;
		this.isAssigned = isAssigned;
		this.isReleasable = isReleasable;
		this.isPersistent = isPersistent;
		this.isUsed = isUsed;
	}
}