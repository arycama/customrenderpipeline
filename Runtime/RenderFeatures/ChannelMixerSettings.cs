using System;
using UnityEngine;

[Serializable]
public class ChannelMixerSettings
{
    [field: SerializeField] public Float3 Red { get; private set; } = Float3.Right;
    [field: SerializeField] public Float3 Green { get; private set; } = Float3.Up;
    [field: SerializeField] public Float3 Blue { get; private set; } = Float3.Forward;
}
