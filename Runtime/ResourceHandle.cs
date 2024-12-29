public class ResourceHandle<T>
{
    public int HandleIndex { get; } // Keep
    public bool IsPersistent { get; } // ?

    public ResourceHandle(int handleIndex, bool isPersistent)
    {
        HandleIndex = handleIndex;
        IsPersistent = isPersistent;
    }
}
