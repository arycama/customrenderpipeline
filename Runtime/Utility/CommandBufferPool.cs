using UnityEngine.Pool;
using UnityEngine.Rendering;

public class CommandBufferPool
{
	private static readonly ObjectPool<CommandBuffer> pool = new(() => new CommandBuffer(), null, propertyBlock => propertyBlock.Clear());

	public static CommandBuffer Get(string name = default)
	{
		var command = pool.Get();
		command.name = name;
		return command;
	}

	public static PooledObject<CommandBuffer> Get(out CommandBuffer value, string name = default)
	{
		var result = pool.Get(out value);
		value.name = name;
		return result;
	}

	public static void Release(CommandBuffer toRelease) => pool.Release(toRelease);
}
