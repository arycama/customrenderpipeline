using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class IndexedShaderPropertyId
    {
        private readonly List<int> properties = new();
        private readonly string id;

        public IndexedShaderPropertyId(string id)
        {
            this.id = id;
        }

        public int GetProperty(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(index.ToString());

            while (properties.Count <= index)
                properties.Add(Shader.PropertyToID($"{id}{properties.Count}"));

            return properties[index];
        }
    }
}