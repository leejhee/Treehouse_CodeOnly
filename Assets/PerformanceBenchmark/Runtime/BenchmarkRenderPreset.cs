#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TreeHouse.PerformanceBenchmark
{
    public enum BenchmarkRenderPreset
    {
        Current = 0,
        NoAdditionalLightShadows = 1,
        RenderScale80 = 2,
        NoAdditionalLightShadowsRenderScale80 = 3,
        GrassGpuInstancing = 4,
        GrassShadowCastingOff = 5,
        GrassRenderingOff = 6
    }

    public struct BenchmarkRenderSnapshot
    {
        public string PresetId;
        public string PresetDisplayName;
        public string PipelineName;
        public int QualityLevel;
        public string QualityName;
        public float RenderScale;
        public bool MainLightShadows;
        public bool AdditionalLightShadows;
        public int ShadowedAdditionalLightCount;
        public bool SupportsGpuInstancing;
        public bool GrassGpuInstancing;
        public int GrassInstancingMaterialCount;
        public int GrassInstancingRendererCount;
        public int GrassShadowCasterRendererCount;
        public int GrassVisibleRendererCount;
        public int GrassStaticBatchRendererCount;
        public int GrassSourceRendererCount;
        public int GrassRuntimeRendererCount;
        public int GrassCombinedChunkCount;
    }

    public sealed class BenchmarkRenderPresetSession : IDisposable
    {
        private struct LightShadowState
        {
            public Light Light;
            public LightShadows Shadows;
        }

        private struct MaterialInstancingState
        {
            public Material Material;
            public bool EnableInstancing;
        }

        private struct RendererShadowCastingState
        {
            public MeshRenderer Renderer;
            public ShadowCastingMode ShadowCastingMode;
        }

        private struct RendererEnabledState
        {
            public MeshRenderer Renderer;
            public bool Enabled;
        }

        private static readonly HashSet<string> GrassMaterialNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Plate1",
                "Plate2",
                "Plate3"
            };

        private RenderPipelineAsset _originalQualityPipeline;
        private UniversalRenderPipelineAsset _runtimePipeline;
        private readonly List<LightShadowState> _modifiedLights =
            new List<LightShadowState>();
        private readonly List<MaterialInstancingState> _modifiedMaterials =
            new List<MaterialInstancingState>();
        private readonly List<RendererShadowCastingState> _modifiedGrassRenderers =
            new List<RendererShadowCastingState>();
        private readonly List<RendererEnabledState> _modifiedGrassRendererVisibility =
            new List<RendererEnabledState>();
        private bool _isApplied;

        public bool TryApply(
            BenchmarkRenderPreset preset,
            out BenchmarkRenderSnapshot snapshot,
            out string failureReason)
        {
            Restore();

            UniversalRenderPipelineAsset sourcePipeline =
                GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (sourcePipeline == null)
            {
                snapshot = default(BenchmarkRenderSnapshot);
                failureReason = "active render pipeline is not URP";
                return false;
            }

            List<MeshRenderer> grassRenderers = FindActiveGrassRenderers();
            HashSet<Material> grassMaterials = FindGrassMaterials(grassRenderers);
            if (grassMaterials.Count != 3 || grassRenderers.Count <= 0)
            {
                snapshot = default(BenchmarkRenderSnapshot);
                failureReason = "expected 3 active grass materials and at least 1 renderer; found " +
                    grassMaterials.Count + " materials and " + grassRenderers.Count + " renderers";
                return false;
            }
            if (preset == BenchmarkRenderPreset.GrassGpuInstancing &&
                !SystemInfo.supportsInstancing)
            {
                snapshot = default(BenchmarkRenderSnapshot);
                failureReason = "GPU instancing is not supported on this device";
                return false;
            }

            _originalQualityPipeline = QualitySettings.renderPipeline;
            _runtimePipeline = UnityEngine.Object.Instantiate(sourcePipeline);
            _runtimePipeline.name = sourcePipeline.name + " (Benchmark Runtime)";

            bool disableAdditionalLightShadows =
                preset == BenchmarkRenderPreset.NoAdditionalLightShadows ||
                preset == BenchmarkRenderPreset.NoAdditionalLightShadowsRenderScale80;
            bool reduceRenderScale =
                preset == BenchmarkRenderPreset.RenderScale80 ||
                preset == BenchmarkRenderPreset.NoAdditionalLightShadowsRenderScale80;
            bool enableGrassGpuInstancing =
                preset == BenchmarkRenderPreset.GrassGpuInstancing;
            bool disableGrassShadowCasting =
                preset == BenchmarkRenderPreset.GrassShadowCastingOff;
            bool disableGrassRendering =
                preset == BenchmarkRenderPreset.GrassRenderingOff;

            if (disableAdditionalLightShadows)
            {
                DisableActiveAdditionalLightShadows();
            }

            if (reduceRenderScale)
            {
                _runtimePipeline.renderScale = 0.8f;
            }

            ConfigureGrassGpuInstancing(grassMaterials, enableGrassGpuInstancing);
            if (disableGrassShadowCasting)
            {
                DisableGrassShadowCasting(grassRenderers);
            }
            if (disableGrassRendering)
            {
                DisableGrassRendering(grassRenderers);
            }

            QualitySettings.renderPipeline = _runtimePipeline;
            _isApplied = true;

            snapshot = CreateSnapshot(preset, _runtimePipeline);
            failureReason = string.Empty;
            return true;
        }

        public static string GetId(BenchmarkRenderPreset preset)
        {
            switch (preset)
            {
                case BenchmarkRenderPreset.NoAdditionalLightShadows:
                    return "R1";
                case BenchmarkRenderPreset.RenderScale80:
                    return "R2";
                case BenchmarkRenderPreset.NoAdditionalLightShadowsRenderScale80:
                    return "R3";
                case BenchmarkRenderPreset.GrassGpuInstancing:
                    return "R4";
                case BenchmarkRenderPreset.GrassShadowCastingOff:
                    return "R5";
                case BenchmarkRenderPreset.GrassRenderingOff:
                    return "R6";
                default:
                    return "R0";
            }
        }

        public static string GetDisplayName(BenchmarkRenderPreset preset)
        {
            switch (preset)
            {
                case BenchmarkRenderPreset.NoAdditionalLightShadows:
                    return "R1 No additional-light shadows";
                case BenchmarkRenderPreset.RenderScale80:
                    return "R2 Render Scale 0.8";
                case BenchmarkRenderPreset.NoAdditionalLightShadowsRenderScale80:
                    return "R3 No add shadows + Scale 0.8";
                case BenchmarkRenderPreset.GrassGpuInstancing:
                    return "R4 Grass GPU Instancing";
                case BenchmarkRenderPreset.GrassShadowCastingOff:
                    return "R5 Grass Shadow Casting Off";
                case BenchmarkRenderPreset.GrassRenderingOff:
                    return "R6 Grass Rendering Off (upper bound)";
                default:
                    return "R0 Baseline (grass instancing off)";
            }
        }

        private static BenchmarkRenderSnapshot CreateSnapshot(
            BenchmarkRenderPreset preset,
            UniversalRenderPipelineAsset pipeline)
        {
            int qualityLevel = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;
            string qualityName = qualityLevel >= 0 && qualityLevel < qualityNames.Length
                ? qualityNames[qualityLevel]
                : "UNKNOWN";
            List<MeshRenderer> grassRenderers = FindActiveGrassRenderers();
            HashSet<Material> grassMaterials = FindGrassMaterials(grassRenderers);
            BenchmarkGrassCombinedChunk[] combinedChunks =
                UnityEngine.Object.FindObjectsOfType<BenchmarkGrassCombinedChunk>();
            int combinedSourceRendererCount = 0;
            foreach (BenchmarkGrassCombinedChunk chunk in combinedChunks)
            {
                combinedSourceRendererCount += chunk.SourceRendererCount;
            }
            bool grassGpuInstancing = grassMaterials.Count > 0;
            foreach (Material material in grassMaterials)
            {
                grassGpuInstancing &= material.enableInstancing;
            }

            return new BenchmarkRenderSnapshot
            {
                PresetId = GetId(preset),
                PresetDisplayName = GetDisplayName(preset),
                PipelineName = pipeline.name,
                QualityLevel = qualityLevel,
                QualityName = qualityName,
                RenderScale = pipeline.renderScale,
                MainLightShadows = pipeline.supportsMainLightShadows,
                AdditionalLightShadows =
                    pipeline.supportsAdditionalLightShadows &&
                    CountActiveShadowedAdditionalLights() > 0,
                ShadowedAdditionalLightCount = CountActiveShadowedAdditionalLights(),
                SupportsGpuInstancing = SystemInfo.supportsInstancing,
                GrassGpuInstancing = grassGpuInstancing,
                GrassInstancingMaterialCount = grassMaterials.Count,
                GrassInstancingRendererCount = grassRenderers.Count,
                GrassShadowCasterRendererCount =
                    CountGrassShadowCasters(grassRenderers),
                GrassVisibleRendererCount = CountVisibleGrassRenderers(grassRenderers),
                GrassStaticBatchRendererCount =
                    CountStaticBatchedGrassRenderers(grassRenderers),
                GrassSourceRendererCount = combinedChunks.Length > 0
                    ? combinedSourceRendererCount
                    : grassRenderers.Count,
                GrassRuntimeRendererCount = grassRenderers.Count,
                GrassCombinedChunkCount = combinedChunks.Length
            };
        }

        private void ConfigureGrassGpuInstancing(
            IEnumerable<Material> materials,
            bool enabled)
        {
            foreach (Material material in materials)
            {
                _modifiedMaterials.Add(new MaterialInstancingState
                {
                    Material = material,
                    EnableInstancing = material.enableInstancing
                });
                material.enableInstancing = enabled;
            }
        }

        private void DisableGrassShadowCasting(IEnumerable<MeshRenderer> renderers)
        {
            foreach (MeshRenderer renderer in renderers)
            {
                _modifiedGrassRenderers.Add(new RendererShadowCastingState
                {
                    Renderer = renderer,
                    ShadowCastingMode = renderer.shadowCastingMode
                });
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private void DisableGrassRendering(IEnumerable<MeshRenderer> renderers)
        {
            foreach (MeshRenderer renderer in renderers)
            {
                _modifiedGrassRendererVisibility.Add(new RendererEnabledState
                {
                    Renderer = renderer,
                    Enabled = renderer.enabled
                });
                renderer.enabled = false;
            }
        }

        private static List<MeshRenderer> FindActiveGrassRenderers()
        {
            List<MeshRenderer> grassRenderers = new List<MeshRenderer>();
            MeshRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
            {
                if (IsTargetGrassMaterial(renderer.sharedMaterial))
                {
                    grassRenderers.Add(renderer);
                }
            }

            return grassRenderers;
        }

        private static HashSet<Material> FindGrassMaterials(
            IEnumerable<MeshRenderer> renderers)
        {
            HashSet<Material> materials = new HashSet<Material>();
            foreach (MeshRenderer renderer in renderers)
            {
                materials.Add(renderer.sharedMaterial);
            }

            return materials;
        }

        private static int CountGrassShadowCasters(
            IEnumerable<MeshRenderer> renderers)
        {
            int count = 0;
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountVisibleGrassRenderers(
            IEnumerable<MeshRenderer> renderers)
        {
            int count = 0;
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer.enabled)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountStaticBatchedGrassRenderers(
            IEnumerable<MeshRenderer> renderers)
        {
            int count = 0;
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer.isPartOfStaticBatch)
                {
                    count++;
                }
            }

            return count;
        }

        public static bool IsTargetGrassMaterial(Material material)
        {
            return material != null && GrassMaterialNames.Contains(material.name);
        }

        private void DisableActiveAdditionalLightShadows()
        {
            Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
            foreach (Light light in lights)
            {
                if (!IsActiveShadowedAdditionalLight(light))
                {
                    continue;
                }

                _modifiedLights.Add(new LightShadowState
                {
                    Light = light,
                    Shadows = light.shadows
                });
                light.shadows = LightShadows.None;
            }
        }

        private static int CountActiveShadowedAdditionalLights()
        {
            int count = 0;
            Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
            foreach (Light light in lights)
            {
                if (IsActiveShadowedAdditionalLight(light))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsActiveShadowedAdditionalLight(Light light)
        {
            return light != null &&
                light.isActiveAndEnabled &&
                (light.type == LightType.Point || light.type == LightType.Spot) &&
                light.shadows != LightShadows.None;
        }

        public void Restore()
        {
            if (!_isApplied)
            {
                return;
            }

            QualitySettings.renderPipeline = _originalQualityPipeline;
            foreach (LightShadowState state in _modifiedLights)
            {
                if (state.Light != null)
                {
                    state.Light.shadows = state.Shadows;
                }
            }
            _modifiedLights.Clear();

            foreach (MaterialInstancingState state in _modifiedMaterials)
            {
                if (state.Material != null)
                {
                    state.Material.enableInstancing = state.EnableInstancing;
                }
            }
            _modifiedMaterials.Clear();

            foreach (RendererShadowCastingState state in _modifiedGrassRenderers)
            {
                if (state.Renderer != null)
                {
                    state.Renderer.shadowCastingMode = state.ShadowCastingMode;
                }
            }
            _modifiedGrassRenderers.Clear();

            foreach (RendererEnabledState state in _modifiedGrassRendererVisibility)
            {
                if (state.Renderer != null)
                {
                    state.Renderer.enabled = state.Enabled;
                }
            }
            _modifiedGrassRendererVisibility.Clear();

            if (_runtimePipeline != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_runtimePipeline);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_runtimePipeline);
                }
            }

            _runtimePipeline = null;
            _originalQualityPipeline = null;
            _isApplied = false;
        }

        public void Dispose()
        {
            Restore();
        }
    }
}
#endif
