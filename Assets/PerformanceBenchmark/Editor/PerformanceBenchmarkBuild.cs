#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TreeHouse.PerformanceBenchmark.Editor
{
    public static class PerformanceBenchmarkBuild
    {
        private enum GrassBuildOptimization
        {
            None,
            StaticBatching,
            SpatialMeshCombining
        }

        private const string BuildRoot = "Builds/Performance";
        private const string RealIngameScenePath = "Assets/Scenes/RealIngame.unity";
        private const string TemporarySceneFolder =
            "Assets/PerformanceBenchmark/__GrassBatchBuildTemp";
        private const string TemporaryScenePath =
            TemporarySceneFolder + "/RealIngame.unity";
        private const string TemporaryMeshFolder = TemporarySceneFolder + "/Meshes";
        private const float GrassChunkSizeMeters = 20f;
        private const int MaxGrassSourcesPerMesh = 512;
        private static readonly string[] GrassMaterialPaths =
        {
            "Assets/3DResources/forest/최종/LastWork/Grass/Texture/Plate1.mat",
            "Assets/3DResources/forest/최종/LastWork/Grass/Texture/Plate2.mat",
            "Assets/3DResources/forest/최종/LastWork/Grass/Texture/Plate3.mat"
        };

        [MenuItem("TreeHouse/Performance/Build Baseline A")]
        public static void BuildBaselineA()
        {
            Build("A", "com.expstudio.treehouse.perf.a");
        }

        [MenuItem("TreeHouse/Performance/Build Optimized B")]
        public static void BuildOptimizedB()
        {
            Build("B", "com.expstudio.treehouse.perf.b");
        }

        [MenuItem("TreeHouse/Performance/Build FPS Diagnostic")]
        public static void BuildFpsDiagnostic()
        {
            Build("FPS", "com.expstudio.treehouse.perf.fps");
        }

        [MenuItem("TreeHouse/Performance/Build FPS Grass Static Batch Candidate")]
        public static void BuildFpsGrassStaticBatchCandidate()
        {
            Build(
                "FPSGB",
                "com.expstudio.treehouse.perf.fps.grassbatch",
                GrassBuildOptimization.StaticBatching);
        }

        [MenuItem("TreeHouse/Performance/Build FPS Grass Spatial Combine Candidate")]
        public static void BuildFpsGrassSpatialCombineCandidate()
        {
            Build(
                "FPSGC",
                "com.expstudio.treehouse.perf.fps.grasscombine",
                GrassBuildOptimization.SpatialMeshCombining);
        }

        private static void Build(
            string variant,
            string applicationIdentifier,
            GrassBuildOptimization grassOptimization = GrassBuildOptimization.None)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0 || !scenes.Contains(RealIngameScenePath))
            {
                throw new InvalidOperationException("RealIngame must be enabled in Build Settings.");
            }

            string originalIdentifier = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            string originalVersion = PlayerSettings.bundleVersion;
            bool originalFrameTimingStats = PlayerSettings.enableFrameTimingStats;
            AndroidArchitecture originalArchitectures = PlayerSettings.Android.targetArchitectures;
            ScriptingImplementation originalBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android);
            bool originalBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
            bool originalUseCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            bool originalStaticBatching = GetStaticBatchingForPlatform(BuildTarget.Android);
            Dictionary<Material, bool> originalGrassInstancing =
                new Dictionary<Material, bool>();
            SceneSetup[] originalSceneSetup = null;

            string outputDirectory = Path.GetFullPath(BuildRoot);
            string outputPath = Path.Combine(outputDirectory, "TreeHouse_Perf_" + variant + ".apk");

            try
            {
                Directory.CreateDirectory(outputDirectory);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, applicationIdentifier);
                PlayerSettings.bundleVersion = "perf-" + variant + "-" + originalVersion;
                PlayerSettings.enableFrameTimingStats = true;
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.SetScriptingBackend(
                    BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.useCustomKeystore = false;
                EditorUserBuildSettings.buildAppBundle = false;
                SetStaticBatchingForPlatform(
                    BuildTarget.Android,
                    grassOptimization == GrassBuildOptimization.StaticBatching);

                if (grassOptimization != GrassBuildOptimization.None)
                {
                    EnsureOpenScenesAreSaved();
                    originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
                    if (grassOptimization == GrassBuildOptimization.StaticBatching)
                    {
                        PrepareGrassStaticBatchScene();
                    }
                    else
                    {
                        PrepareGrassSpatialCombineScene();
                    }
                    scenes = scenes
                        .Select(path => path == RealIngameScenePath
                            ? TemporaryScenePath
                            : path)
                        .ToArray();
                    EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
                    originalSceneSetup = null;
                }

                if (variant == "FPS" || variant == "FPSGB" || variant == "FPSGC")
                {
                    foreach (string materialPath in GrassMaterialPaths)
                    {
                        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if (material == null)
                        {
                            throw new InvalidOperationException(
                                "FPS diagnostic grass material is missing: " + materialPath);
                        }

                        originalGrassInstancing.Add(material, material.enableInstancing);
                        material.enableInstancing = true;
                        EditorUtility.SetDirty(material);
                    }
                    AssetDatabase.SaveAssets();
                }

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Performance APK build failed: " + report.summary.result);
                }

                Debug.Log("[TreeHouse Benchmark] Built " + outputPath);
                if (!Application.isBatchMode)
                {
                    EditorUtility.RevealInFinder(outputPath);
                }
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, originalIdentifier);
                PlayerSettings.bundleVersion = originalVersion;
                PlayerSettings.enableFrameTimingStats = originalFrameTimingStats;
                PlayerSettings.Android.targetArchitectures = originalArchitectures;
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, originalBackend);
                PlayerSettings.Android.useCustomKeystore = originalUseCustomKeystore;
                EditorUserBuildSettings.buildAppBundle = originalBuildAppBundle;
                SetStaticBatchingForPlatform(BuildTarget.Android, originalStaticBatching);
                foreach (KeyValuePair<Material, bool> grassState in originalGrassInstancing)
                {
                    grassState.Key.enableInstancing = grassState.Value;
                    EditorUtility.SetDirty(grassState.Key);
                }
                AssetDatabase.SaveAssets();

                if (originalSceneSetup != null)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
                }

                if (grassOptimization != GrassBuildOptimization.None &&
                    AssetDatabase.IsValidFolder(TemporarySceneFolder))
                {
                    AssetDatabase.DeleteAsset(TemporarySceneFolder);
                }
            }
        }

        private static void EnsureOpenScenesAreSaved()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty)
                {
                    throw new InvalidOperationException(
                        "Save all open Scenes before building the grass batching candidate. " +
                        "Unsaved Scene: " + scene.name);
                }
            }
        }

        private static void PrepareGrassStaticBatchScene()
        {
            Scene scene = OpenTemporarySceneCopy();
            MeshRenderer[] grassRenderers = FindTargetGrassRenderers(scene);
            if (grassRenderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "No target grass renderers were found in the temporary scene.");
            }

            foreach (MeshRenderer renderer in grassRenderers)
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(
                    renderer.gameObject);
                GameObjectUtility.SetStaticEditorFlags(
                    renderer.gameObject,
                    flags | StaticEditorFlags.BatchingStatic);
            }

            SaveTemporaryScene(scene);
            Debug.Log(
                "[TreeHouse Benchmark] Prepared temporary grass static batch scene. " +
                "Renderers=" + grassRenderers.Length + ", Path=" + TemporaryScenePath);
        }

        private static void PrepareGrassSpatialCombineScene()
        {
            Scene scene = OpenTemporarySceneCopy();
            MeshRenderer[] grassRenderers = FindTargetGrassRenderers(scene);
            if (grassRenderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "No target grass renderers were found in the temporary scene.");
            }

            Dictionary<string, GrassCombineGroup> groups =
                new Dictionary<string, GrassCombineGroup>();
            long sourceVertexCount = 0;
            long sourceIndexCount = 0;

            foreach (MeshRenderer renderer in grassRenderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    throw new InvalidOperationException(
                        "Grass spatial combining requires every target renderer to be active: " +
                        GetHierarchyPath(renderer.transform));
                }
                if (renderer.HasPropertyBlock())
                {
                    throw new InvalidOperationException(
                        "Grass renderer has a MaterialPropertyBlock and cannot be combined safely: " +
                        GetHierarchyPath(renderer.transform));
                }
                if (renderer.additionalVertexStreams != null)
                {
                    throw new InvalidOperationException(
                        "Grass renderer has additional vertex streams and cannot be combined safely: " +
                        GetHierarchyPath(renderer.transform));
                }

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Material[] materials = renderer.sharedMaterials;
                if (mesh == null || mesh.subMeshCount != 1 || materials.Length != 1 ||
                    !BenchmarkRenderPresetSession.IsTargetGrassMaterial(materials[0]))
                {
                    throw new InvalidOperationException(
                        "Grass spatial combining expects one Mesh, one submesh, and one target " +
                        "material per renderer: " + GetHierarchyPath(renderer.transform));
                }

                Bounds bounds = renderer.bounds;
                int cellX = Mathf.FloorToInt(bounds.center.x / GrassChunkSizeMeters);
                int cellZ = Mathf.FloorToInt(bounds.center.z / GrassChunkSizeMeters);
                string key = CreateGrassCombineKey(renderer, materials[0], cellX, cellZ);
                if (!groups.TryGetValue(key, out GrassCombineGroup group))
                {
                    group = new GrassCombineGroup
                    {
                        Key = key,
                        CellX = cellX,
                        CellZ = cellZ,
                        Material = materials[0],
                        Template = renderer
                    };
                    groups.Add(key, group);
                }

                group.Sources.Add(new GrassCombineSource
                {
                    Renderer = renderer,
                    Mesh = mesh
                });
                sourceVertexCount += mesh.vertexCount;
                sourceIndexCount += (long)mesh.GetIndexCount(0);
            }

            string meshFolderGuid = AssetDatabase.CreateFolder(
                TemporarySceneFolder,
                "Meshes");
            if (string.IsNullOrEmpty(meshFolderGuid))
            {
                throw new InvalidOperationException(
                    "Could not create the temporary combined grass Mesh folder.");
            }

            GameObject combinedRoot = new GameObject("TH.GrassSpatialChunks");
            combinedRoot.transform.position = Vector3.zero;
            combinedRoot.transform.rotation = Quaternion.identity;
            combinedRoot.transform.localScale = Vector3.one;

            int combinedRendererCount = 0;
            int combinedSourceCount = 0;
            foreach (GrassCombineGroup group in groups.Values
                .OrderBy(value => value.CellX)
                .ThenBy(value => value.CellZ)
                .ThenBy(value => value.Material.name, StringComparer.Ordinal)
                .ThenBy(value => value.Key, StringComparer.Ordinal))
            {
                for (int offset = 0;
                    offset < group.Sources.Count;
                    offset += MaxGrassSourcesPerMesh)
                {
                    int count = Math.Min(
                        MaxGrassSourcesPerMesh,
                        group.Sources.Count - offset);
                    CreateCombinedGrassRenderer(
                        combinedRoot.transform,
                        group,
                        offset,
                        count,
                        combinedRendererCount);
                    combinedRendererCount++;
                    combinedSourceCount += count;
                }
            }

            foreach (MeshRenderer renderer in grassRenderers)
            {
                // Keep the temporary Scene structurally identical to the source Scene.
                // Removing tens of thousands of components from prefab/Scene instances can
                // produce a very large set of structural overrides in the player Scene.
                // Disabling each source object removes it from runtime rendering and from
                // FindObjectsOfType results while leaving only simple active-state overrides.
                renderer.gameObject.SetActive(false);
            }

            if (combinedSourceCount != grassRenderers.Length)
            {
                throw new InvalidOperationException(
                    "Combined grass source count mismatch. Expected=" +
                    grassRenderers.Length + ", Actual=" + combinedSourceCount + ".");
            }

            SaveTemporaryScene(scene);
            Debug.Log(
                "[TreeHouse Benchmark] Prepared temporary grass spatial combine scene. " +
                "SourceRenderers=" + grassRenderers.Length +
                ", CombinedRenderers=" + combinedRendererCount +
                ", CompatibilityGroups=" + groups.Count +
                ", ChunkSizeMeters=" + GrassChunkSizeMeters +
                ", MaxSourcesPerMesh=" + MaxGrassSourcesPerMesh +
                ", SourceVertices=" + sourceVertexCount +
                ", SourceIndices=" + sourceIndexCount +
                ", Path=" + TemporaryScenePath);
        }

        private static Scene OpenTemporarySceneCopy()
        {
            if (AssetDatabase.IsValidFolder(TemporarySceneFolder))
            {
                if (!AssetDatabase.DeleteAsset(TemporarySceneFolder))
                {
                    throw new InvalidOperationException(
                        "Could not remove stale grass batching build folder: " +
                        TemporarySceneFolder);
                }
            }

            string folderGuid = AssetDatabase.CreateFolder(
                "Assets/PerformanceBenchmark",
                "__GrassBatchBuildTemp");
            if (string.IsNullOrEmpty(folderGuid) ||
                !AssetDatabase.CopyAsset(RealIngameScenePath, TemporaryScenePath))
            {
                throw new InvalidOperationException(
                    "Could not create the temporary grass batching scene.");
            }

            Scene scene = EditorSceneManager.OpenScene(
                TemporaryScenePath,
                OpenSceneMode.Single);
            return scene;
        }

        private static MeshRenderer[] FindTargetGrassRenderers(Scene scene)
        {
            return scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                .Where(renderer =>
                    renderer.sharedMaterials.Any(
                        BenchmarkRenderPresetSession.IsTargetGrassMaterial))
                .ToArray();
        }

        private static void SaveTemporaryScene(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException(
                    "Could not save the temporary grass build scene.");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                TemporaryScenePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static void CreateCombinedGrassRenderer(
            Transform combinedRoot,
            GrassCombineGroup group,
            int sourceOffset,
            int sourceCount,
            int combinedIndex)
        {
            CombineInstance[] instances = new CombineInstance[sourceCount];
            for (int index = 0; index < sourceCount; index++)
            {
                GrassCombineSource source = group.Sources[sourceOffset + index];
                instances[index] = new CombineInstance
                {
                    mesh = source.Mesh,
                    subMeshIndex = 0,
                    transform = source.Renderer.localToWorldMatrix,
                    lightmapScaleOffset = source.Renderer.lightmapScaleOffset,
                    realtimeLightmapScaleOffset =
                        source.Renderer.realtimeLightmapScaleOffset
                };
            }

            Mesh combinedMesh = new Mesh
            {
                name = "TH_GrassChunk_" + combinedIndex.ToString("D4"),
                indexFormat = IndexFormat.UInt32
            };
            bool hasLightmapData = group.Template.lightmapIndex >= 0;
            combinedMesh.CombineMeshes(instances, true, true, hasLightmapData);
            if (combinedMesh.vertexCount == 0)
            {
                UnityEngine.Object.DestroyImmediate(combinedMesh);
                throw new InvalidOperationException(
                    "Mesh.CombineMeshes produced an empty grass chunk " +
                    combinedIndex + ".");
            }
            combinedMesh.RecalculateBounds();

            string meshPath = TemporaryMeshFolder + "/GrassChunk_" +
                combinedIndex.ToString("D4") + ".asset";
            AssetDatabase.CreateAsset(combinedMesh, meshPath);

            GameObject combinedObject = new GameObject(
                "GrassChunk_" + combinedIndex.ToString("D4") +
                "_X" + group.CellX + "_Z" + group.CellZ);
            combinedObject.layer = group.Template.gameObject.layer;
            combinedObject.transform.SetParent(combinedRoot, false);

            MeshFilter targetFilter = combinedObject.AddComponent<MeshFilter>();
            targetFilter.sharedMesh = combinedMesh;
            MeshRenderer targetRenderer = combinedObject.AddComponent<MeshRenderer>();
            targetRenderer.sharedMaterial = group.Material;
            CopyRendererSettings(group.Template, targetRenderer, hasLightmapData);

            BenchmarkGrassCombinedChunk marker =
                combinedObject.AddComponent<BenchmarkGrassCombinedChunk>();
            MonoScript markerScript = MonoScript.FromMonoBehaviour(marker);
            if (markerScript == null ||
                markerScript.GetClass() != typeof(BenchmarkGrassCombinedChunk))
            {
                throw new InvalidOperationException(
                    "BenchmarkGrassCombinedChunk must be a valid MonoBehaviour in its " +
                    "own matching script file before grass chunks can be serialized.");
            }
            marker.SourceRendererCount = sourceCount;
            marker.CellX = group.CellX;
            marker.CellZ = group.CellZ;
        }

        private static void CopyRendererSettings(
            MeshRenderer source,
            MeshRenderer target,
            bool hasLightmapData)
        {
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.staticShadowCaster = source.staticShadowCaster;
            target.motionVectorGenerationMode = source.motionVectorGenerationMode;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.receiveGI = source.receiveGI;
            target.probeAnchor = source.probeAnchor;
            target.lightProbeProxyVolumeOverride = source.lightProbeProxyVolumeOverride;
            target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            target.renderingLayerMask = source.renderingLayerMask;
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
            target.lightmapIndex = source.lightmapIndex;
            target.realtimeLightmapIndex = source.realtimeLightmapIndex;
            if (hasLightmapData)
            {
                target.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
                target.realtimeLightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
            }
        }

        private static string CreateGrassCombineKey(
            MeshRenderer renderer,
            Material material,
            int cellX,
            int cellZ)
        {
            return string.Join(
                "|",
                cellX,
                cellZ,
                material.GetInstanceID(),
                renderer.gameObject.layer,
                (int)renderer.shadowCastingMode,
                renderer.receiveShadows,
                renderer.staticShadowCaster,
                (int)renderer.motionVectorGenerationMode,
                (int)renderer.lightProbeUsage,
                (int)renderer.reflectionProbeUsage,
                (int)renderer.receiveGI,
                GetInstanceId(renderer.probeAnchor),
                GetInstanceId(renderer.lightProbeProxyVolumeOverride),
                renderer.allowOcclusionWhenDynamic,
                renderer.renderingLayerMask,
                renderer.sortingLayerID,
                renderer.sortingOrder,
                renderer.lightmapIndex,
                renderer.realtimeLightmapIndex);
        }

        private static int GetInstanceId(UnityEngine.Object value)
        {
            return value != null ? value.GetInstanceID() : 0;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private sealed class GrassCombineSource
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        private sealed class GrassCombineGroup
        {
            public string Key;
            public int CellX;
            public int CellZ;
            public Material Material;
            public MeshRenderer Template;
            public readonly List<GrassCombineSource> Sources =
                new List<GrassCombineSource>();
        }

        private static bool GetStaticBatchingForPlatform(BuildTarget target)
        {
            MethodInfo publicGetter = typeof(PlayerSettings).GetMethod(
                "GetStaticBatchingForPlatform",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(BuildTarget) },
                null);
            if (publicGetter != null)
            {
                return (bool)publicGetter.Invoke(null, new object[] { target });
            }

            MethodInfo legacyGetter = GetLegacyBatchingMethod(
                "GetBatchingForPlatform",
                typeof(BuildTarget),
                typeof(int).MakeByRefType(),
                typeof(int).MakeByRefType());
            object[] arguments = { target, 0, 0 };
            legacyGetter.Invoke(null, arguments);
            return (int)arguments[1] != 0;
        }

        private static void SetStaticBatchingForPlatform(
            BuildTarget target,
            bool enabled)
        {
            MethodInfo publicSetter = typeof(PlayerSettings).GetMethod(
                "SetStaticBatchingForPlatform",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(BuildTarget), typeof(bool) },
                null);
            if (publicSetter != null)
            {
                publicSetter.Invoke(null, new object[] { target, enabled });
                return;
            }

            MethodInfo legacyGetter = GetLegacyBatchingMethod(
                "GetBatchingForPlatform",
                typeof(BuildTarget),
                typeof(int).MakeByRefType(),
                typeof(int).MakeByRefType());
            object[] getArguments = { target, 0, 0 };
            legacyGetter.Invoke(null, getArguments);

            MethodInfo legacySetter = GetLegacyBatchingMethod(
                "SetBatchingForPlatform",
                typeof(BuildTarget),
                typeof(int),
                typeof(int));
            legacySetter.Invoke(
                null,
                new[]
                {
                    (object)target,
                    enabled ? 1 : 0,
                    getArguments[2]
                });
        }

        private static MethodInfo GetLegacyBatchingMethod(
            string methodName,
            params Type[] parameterTypes)
        {
            MethodInfo method = typeof(PlayerSettings).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                parameterTypes,
                null);
            if (method == null)
            {
                throw new NotSupportedException(
                    "This Unity version does not expose PlayerSettings." + methodName + ".");
            }

            return method;
        }
    }
}
#endif
