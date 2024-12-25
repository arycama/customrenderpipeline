public class ResourceHandle<T>
{
    public T Resource { get; set; }
    public int ResourceIndex { get; set; }
    public int HandleIndex { get; set; }
    public bool IsPersistent { get; set; }
    public bool IsCreated { get; set; }
    public bool IsImported { get; set; }
    public bool IsNotReleasable { get; set; }
}
