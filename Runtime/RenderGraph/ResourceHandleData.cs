using UnityEngine.Assertions;

public struct ResourceHandleData<V, T> where V : IResourceDescriptor<T>
{
	public int createIndex;
	public int createIndex1;
	public int freeIndex;
	public int freeIndex1;
	public int resourceIndex;
	public V descriptor;
	public bool isAssigned;
	public bool isPersistent;
	public bool isUsed;

	public ResourceHandleData(int createIndex, int freeIndex, int resourceIndex, V descriptor, bool isAssigned, bool isPersistent, bool isUsed)
	{
		this.createIndex = createIndex;
		this.freeIndex = freeIndex;
		this.resourceIndex = resourceIndex;
		this.descriptor = descriptor;
		this.isAssigned = isAssigned;
		this.isPersistent = isPersistent;
		this.isUsed = isUsed;
        createIndex1 = -1;
        freeIndex1 = -1;
    }
}