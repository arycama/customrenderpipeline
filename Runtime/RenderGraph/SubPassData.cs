using Unity.Collections;
using UnityEngine.Rendering;

public struct SubPassData
{
    public NativeList<int> inputs, outputs;
    public SubPassFlags flags;

    public SubPassData(NativeList<int> inputs, NativeList<int> outputs, SubPassFlags flags)
    {
        this.inputs = inputs;
        this.outputs = outputs;
        this.flags = flags;
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