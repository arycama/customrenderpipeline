public class ResourceHandle<T>
{
    public T Resource { get; set; } // Can fetch using resource index instead of storing it on the handle
    public int HandleIndex { get; } // Keep
    public bool IsPersistent { get; } // ?
    public bool IsAssigned { get; set; } // Might be able to simplify
    public bool IsImported { get; } // Can maybe just be inferred when handleIndex == -1?
    public bool IsNotReleasable { get; set; } // Instead of this, maybe just have a resourceHanldeSystem.FreePersistentResource that removes it from an array or something

    public ResourceHandle(int handleIndex, bool isImported, bool isPersistent)
    {
        HandleIndex = handleIndex;
        IsImported = isImported;
        IsPersistent = isPersistent;
        IsNotReleasable = isPersistent;
        IsAssigned = isImported; // Imported textures are already created

    }
}
