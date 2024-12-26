﻿public class ResourceHandle<T>
{
    public T Resource { get; set; }
    public int ResourceIndex { get; set; }
    public int HandleIndex { get; }
    public bool IsPersistent { get; }
    public bool IsCreated { get; set; }
    public bool IsImported { get; }
    public bool IsNotReleasable { get; set; }

    public ResourceHandle(int handleIndex, bool isImported, bool isPersistent)
    {
        HandleIndex = handleIndex;
        IsImported = isImported;
        IsPersistent = isPersistent;
        IsNotReleasable = isPersistent;
        IsCreated = isImported; // Imported textures are already created

    }
}
