public interface IResourceHandle<K>
{
    int Index { get; }
    bool IsPersistent { get; }
    K Descriptor { get; }
}
