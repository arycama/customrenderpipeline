public class ResourceHandle<T>
{
    public int HandleIndex { get; } // Keep
    public bool IsImported { get; } // Could maybe use negative index?
    public bool IsPersistent { get; } // ?
    public bool IsAssigned { get; set; } // Might be able to simplify
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
