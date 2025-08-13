using UnityEngine;
using UnityEngine.Pool;

public class PropertyBlockPool
{
	private static readonly ObjectPool<MaterialPropertyBlock> pool = new(() => new MaterialPropertyBlock(), null, propertyBlock => propertyBlock.Clear());

	public static MaterialPropertyBlock Get() => pool.Get();

	public static PooledObject<MaterialPropertyBlock> Get(out MaterialPropertyBlock value) => pool.Get(out value);

	public static void Release(MaterialPropertyBlock toRelease) => pool.Release(toRelease);
}
