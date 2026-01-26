using Unity.Collections;
using UnityEngine.Rendering;

public struct SubPassData
{
    public NativeList<int> inputs, outputs;
    public SubPassFlags flags;

    public static SubPassData Create()
    {
        return new SubPassData { inputs = new NativeList<int>(8, Allocator.Temp), outputs = new NativeList<int>(8, Allocator.Temp) };
    }

    public SubPassDescriptor Descriptor => new()
    {
        flags = flags,
        colorOutputs = new(outputs.AsArray()),
        inputs = new(inputs.AsArray())
    };

    public void AddInput(int index)
    {
        inputs.Add(index);
    }

    public void AddOutput(int index)
    {
        outputs.Add(index);
    }

    public void Clear()
    {
        inputs.Clear();
        outputs.Clear();
        flags = SubPassFlags.None;
    }
}