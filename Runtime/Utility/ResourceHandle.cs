using System;

public readonly struct ResourceHandle<T> : IEquatable<ResourceHandle<T>>
{
	public readonly int index;

	public ResourceHandle(int index)
	{
		this.index = index;
	}

    public static bool operator ==(ResourceHandle<T> left, ResourceHandle<T> right) => left.index == right.index;

    public static bool operator !=(ResourceHandle<T> left, ResourceHandle<T> right) => left.index != right.index;

    public override bool Equals(object obj) => obj is ResourceHandle<T> handle && Equals(handle);

    public bool Equals(ResourceHandle<T> other) => index == other.index;

    public override int GetHashCode() => index;
}