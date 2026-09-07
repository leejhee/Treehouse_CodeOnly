#if DEVELOPMENT_BUILD || UNITY_EDITOR
namespace TreeHouse.PerformanceBenchmark
{
    /// <summary>
    /// Per-frame counters shared by benchmark-instrumented gameplay code.
    /// The recorder reads and resets them from LateUpdate.
    /// </summary>
    public static class BenchmarkRuntimeCounters
    {
        public static int AudioUpdateCalls;
        public static int ArrangeCandidatesChecked;

        public static void ResetFrame()
        {
            AudioUpdateCalls = 0;
            ArrangeCandidatesChecked = 0;
        }
    }
}
#endif
