#if DEVELOPMENT_BUILD || UNITY_EDITOR
using UnityEngine;

namespace TreeHouse.PerformanceBenchmark
{
    [DisallowMultipleComponent]
    public sealed class BenchmarkGrassCombinedChunk : MonoBehaviour
    {
        public int SourceRendererCount;
        public int CellX;
        public int CellZ;
    }
}
#endif
