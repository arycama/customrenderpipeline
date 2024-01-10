using UnityEngine.Rendering;

public class CullingResultsHandle
{
    public CullingResults CullingResults { get; set; }

    public static implicit operator CullingResults(CullingResultsHandle cullingResultsHandle) => cullingResultsHandle.CullingResults;
}
