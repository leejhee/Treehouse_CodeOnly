#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;

namespace TreeHouse.PerformanceBenchmark
{
    [Serializable]
    public sealed class BenchmarkRunMetadata
    {
        public string runId;
        public string variant;
        public string pairId;
        public string pairOrder;
        public string scenarioId;
        public string scenarioFixtureVersion;
        public int deterministicSeed;
        public int fixtureFieldObjectCount;
        public int fixtureHouseFurnitureCount;
        public int fixtureHouseCarpetCount;
        public string applicationVersion;
        public string applicationIdentifier;
        public string unityVersion;
        public string deviceModel;
        public string operatingSystem;
        public string graphicsDevice;
        public string graphicsDeviceType;
        public string metricsSchemaVersion;
        public bool unityProfilerRawEnabled;
        public string renderPresetId;
        public string renderPresetDisplayName;
        public string renderPipelineName;
        public int qualityLevel;
        public string qualityName;
        public int targetFrameRate;
        public int screenWidth;
        public int screenHeight;
        public float screenRefreshRateHz;
        public float renderScale;
        public bool mainLightShadows;
        public bool additionalLightShadows;
        public int shadowedAdditionalLightCount;
        public bool supportsGpuInstancing;
        public bool grassGpuInstancing;
        public int grassInstancingMaterialCount;
        public int grassInstancingRendererCount;
        public int grassShadowCasterRendererCount;
        public int grassVisibleRendererCount;
        public bool grassStaticBatchingExpected;
        public int grassStaticBatchRendererCount;
        public bool grassMeshCombiningExpected;
        public int grassSourceRendererCount;
        public int grassRuntimeRendererCount;
        public int grassCombinedChunkCount;
        public string runRequestedAtUtc;
        public string warmupStartedAtUtc;
        public string warmupCompletedAtUtc;
        public string captureStartedAtUtc;
        public string captureCompletedAtUtc;
        public string runCompletedAtUtc;
        public float ambientTemperatureC;
        public float referenceTemperatureC;
        public float temperatureToleranceC;
        public float batteryPercentPreWarmup;
        public float batteryTemperaturePreWarmupC;
        public int thermalStatusPreWarmup;
        public float thermalHeadroomPreWarmup;
        public int batteryPluggedPreWarmup;
        public string powerSourcePreWarmup;
        public bool externalPowerConnectedPreWarmup;
        public float batteryPercentStart;
        public float batteryTemperatureStartC;
        public int thermalStatusStart;
        public float thermalHeadroomStart;
        public int batteryPluggedStart;
        public string powerSourceStart;
        public bool externalPowerConnectedStart;
        public float warmupTemperatureRiseC;
        public float batteryPercentEnd;
        public float batteryTemperatureEndC;
        public int thermalStatusEnd;
        public float thermalHeadroomEnd;
        public int batteryPluggedEnd;
        public string powerSourceEnd;
        public bool externalPowerConnectedEnd;
        public int capturedFrames;
        public bool valid;
        public string invalidReason;
    }
}
#endif
