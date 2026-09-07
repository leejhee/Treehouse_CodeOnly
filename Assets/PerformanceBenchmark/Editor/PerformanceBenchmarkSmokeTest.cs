#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TreeHouse.PerformanceBenchmark.Editor
{
    [InitializeOnLoad]
    public static class PerformanceBenchmarkSmokeTest
    {
        private const string ActiveKey = "TH.Perf.Smoke.Active";
        private const string PhaseKey = "TH.Perf.Smoke.Phase";
        private const string StartedAtKey = "TH.Perf.Smoke.StartedAt";
        private const string ScenePath = "Assets/Scenes/RealIngame.unity";
        private const double SceneSettleSeconds = 3.0;
        private const double TimeoutSeconds = 90.0;

        static PerformanceBenchmarkSmokeTest()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("TreeHouse/Performance/Run Scenario Smoke Test")]
        public static void RunFromMenu()
        {
            StartSmokeTest();
        }

        public static void RunFromCommandLine()
        {
            StartSmokeTest();
        }

        private static void StartSmokeTest()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                throw new InvalidOperationException("Benchmark smoke test is already active.");
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(PhaseKey, "request-play");
            SessionState.SetFloat(StartedAtKey, (float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }

            string phase = SessionState.GetString(PhaseKey, "request-play");
            double elapsed = EditorApplication.timeSinceStartup -
                SessionState.GetFloat(StartedAtKey, (float)EditorApplication.timeSinceStartup);

            if (elapsed > TimeoutSeconds)
            {
                Finish(1, "Timed out waiting for the benchmark smoke test.");
                return;
            }

            if (phase == "request-play")
            {
                if (EditorApplication.isPlaying)
                {
                    SessionState.SetString(PhaseKey, "settle");
                    SessionState.SetFloat(StartedAtKey, (float)EditorApplication.timeSinceStartup);
                }
                return;
            }

            if (phase == "settle" && EditorApplication.isPlaying &&
                elapsed >= SceneSettleSeconds)
            {
                try
                {
                    ValidateScenarioRuntime();
                    Finish(0, "Scenario smoke test passed.");
                }
                catch (Exception exception)
                {
                    Finish(1, exception.ToString());
                }
                return;
            }

            if (phase == "stopping" && !EditorApplication.isPlaying)
            {
                int exitCode = SessionState.GetInt("TH.Perf.Smoke.ExitCode", 1);
                string message = SessionState.GetString("TH.Perf.Smoke.Message", string.Empty);
                SessionState.EraseBool(ActiveKey);
                SessionState.EraseString(PhaseKey);
                SessionState.EraseInt("TH.Perf.Smoke.ExitCode");
                SessionState.EraseString("TH.Perf.Smoke.Message");

                if (exitCode == 0)
                {
                    Debug.Log("[TreeHouse Benchmark] " + message);
                }
                else
                {
                    Debug.LogError("[TreeHouse Benchmark] " + message);
                }

                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(exitCode);
                }
            }
        }

        private static void ValidateScenarioRuntime()
        {
            if (GameManager.Instance == null || GameManager.Instance.saveData == null)
            {
                throw new InvalidOperationException("GameManager benchmark save was not prepared.");
            }

            ValidateRenderDiagnosticPresets();

            int fieldObjectCount = GameManager.Instance.saveData.fieldItemList.Sum(item => item.count);
            if (fieldObjectCount != 62)
            {
                throw new InvalidOperationException(
                    "Expected 62 field fixture objects, found " + fieldObjectCount + ".");
            }
            if (GameManager.Instance.saveData.houseList.Count != 1 ||
                GameManager.Instance.saveData.houseList[0].furnitureList.Count != 24)
            {
                throw new InvalidOperationException("House fixture save does not contain 24 furniture entries.");
            }

            if (!BenchmarkScenarioFixture.TryApply(BenchmarkScenario.FieldStatic, out string fieldFailure))
            {
                throw new InvalidOperationException("Field scenario failed: " + fieldFailure);
            }
            if (!WarpManager.instance.isField || !WarpManager.instance.fieldRoot.activeSelf ||
                WarpManager.instance.houseRoot.activeSelf)
            {
                throw new InvalidOperationException("Field scenario root state is invalid.");
            }

            ValidateWarpRaycastBuffer();
            ValidateAudioDistanceFade();

            if (!BenchmarkScenarioFixture.TryApply(
                    BenchmarkScenario.HouseMixed24Static, out string houseFailure))
            {
                throw new InvalidOperationException("House scenario failed: " + houseFailure);
            }

            int loadedFurniture = ArrangeManager.instance.fixedFurnitureList.Count;
            int loadedCarpets = ArrangeManager.instance.fixedFurnitureList.Count(
                furniture => furniture.data.id == 6 || furniture.data.id == 17);
            if (loadedFurniture != 24 || loadedCarpets != 4)
            {
                throw new InvalidOperationException(
                    "Loaded house fixture mismatch. Furniture=" + loadedFurniture +
                    ", carpets=" + loadedCarpets + ".");
            }
            if (WarpManager.instance.isField || WarpManager.instance.fieldRoot.activeSelf ||
                !WarpManager.instance.houseRoot.activeSelf)
            {
                throw new InvalidOperationException("House scenario root state is invalid.");
            }

            ValidateArrangeColliderInvalidation();

            int audioComponents = Resources.FindObjectsOfTypeAll<sfxPlayer>()
                .Count(component => component.gameObject.scene.IsValid());
            Debug.Log(
                "[TreeHouse Benchmark] Runtime fixture validated. FieldObjects=62, " +
                "HouseFurniture=24, Carpets=4, SceneSfxComponents=" + audioComponents +
                ", WarpBufferReuse=PASS, AudioFade=PASS, ArrangeInvalidation=PASS.");

            BenchmarkScenarioFixture.TryApply(BenchmarkScenario.FieldStatic, out _);
        }

        private static void ValidateRenderDiagnosticPresets()
        {
            UniversalRenderPipelineAsset source =
                GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (source == null)
            {
                throw new InvalidOperationException("Render preset smoke test requires URP.");
            }

            RenderPipelineAsset originalQualityPipeline = QualitySettings.renderPipeline;
            float sourceRenderScale = source.renderScale;
            Light[] sourceLights = UnityEngine.Object.FindObjectsOfType<Light>();
            int sourceShadowedAdditionalLights = sourceLights.Count(light =>
                light.isActiveAndEnabled &&
                (light.type == LightType.Point || light.type == LightType.Spot) &&
                light.shadows != LightShadows.None);
            bool sourceAdditionalLightShadows =
                source.supportsAdditionalLightShadows &&
                sourceShadowedAdditionalLights > 0;
            MeshRenderer[] sourceRenderers =
                UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            Material[] sourceGrassMaterials = sourceRenderers
                .Select(renderer => renderer.sharedMaterial)
                .Where(BenchmarkRenderPresetSession.IsTargetGrassMaterial)
                .Distinct()
                .ToArray();
            MeshRenderer[] sourceGrassRenderers = sourceRenderers
                .Where(renderer =>
                    BenchmarkRenderPresetSession.IsTargetGrassMaterial(renderer.sharedMaterial))
                .ToArray();
            int sourceGrassRendererCount = sourceGrassRenderers.Length;
            Dictionary<Material, bool> sourceGrassInstancing =
                sourceGrassMaterials.ToDictionary(
                    material => material,
                    material => material.enableInstancing);
            Dictionary<MeshRenderer, ShadowCastingMode> sourceGrassShadowCasting =
                sourceGrassRenderers.ToDictionary(
                    renderer => renderer,
                    renderer => renderer.shadowCastingMode);
            Dictionary<MeshRenderer, bool> sourceGrassVisibility =
                sourceGrassRenderers.ToDictionary(
                    renderer => renderer,
                    renderer => renderer.enabled);
            int sourceGrassShadowCasterCount = sourceGrassRenderers.Count(renderer =>
                renderer.shadowCastingMode != ShadowCastingMode.Off);
            int sourceVisibleGrassRendererCount = sourceGrassRenderers.Count(renderer =>
                renderer.enabled);
            int sourceStaticBatchedGrassRendererCount = sourceGrassRenderers.Count(renderer =>
                renderer.isPartOfStaticBatch);
            BenchmarkGrassCombinedChunk[] sourceCombinedGrassChunks =
                UnityEngine.Object.FindObjectsOfType<BenchmarkGrassCombinedChunk>();
            int sourceCombinedGrassRendererCount = sourceCombinedGrassChunks.Sum(chunk =>
                chunk.SourceRendererCount);
            if (sourceGrassMaterials.Length != 3 || sourceGrassRendererCount <= 0)
            {
                throw new InvalidOperationException(
                    "Expected three active grass materials and at least one renderer. Materials=" +
                    sourceGrassMaterials.Length + ", Renderers=" + sourceGrassRendererCount + ".");
            }

            BenchmarkRenderPreset[] presets =
            {
                BenchmarkRenderPreset.Current,
                BenchmarkRenderPreset.NoAdditionalLightShadows,
                BenchmarkRenderPreset.RenderScale80,
                BenchmarkRenderPreset.NoAdditionalLightShadowsRenderScale80,
                BenchmarkRenderPreset.GrassGpuInstancing,
                BenchmarkRenderPreset.GrassShadowCastingOff,
                BenchmarkRenderPreset.GrassRenderingOff
            };

            foreach (BenchmarkRenderPreset preset in presets)
            {
                using (BenchmarkRenderPresetSession session = new BenchmarkRenderPresetSession())
                {
                    if (!session.TryApply(preset, out BenchmarkRenderSnapshot snapshot, out string failure))
                    {
                        throw new InvalidOperationException(
                            "Render preset " + preset + " failed: " + failure);
                    }

                    bool expectsScale80 =
                        preset == BenchmarkRenderPreset.RenderScale80 ||
                        preset == BenchmarkRenderPreset.NoAdditionalLightShadowsRenderScale80;
                    bool expectsShadowsOff =
                        preset == BenchmarkRenderPreset.NoAdditionalLightShadows ||
                        preset == BenchmarkRenderPreset.NoAdditionalLightShadowsRenderScale80;
                    float expectedScale = expectsScale80 ? 0.8f : sourceRenderScale;
                    bool expectedAdditionalShadows =
                        expectsShadowsOff ? false : sourceAdditionalLightShadows;
                    int expectedShadowedAdditionalLights =
                        expectsShadowsOff ? 0 : sourceShadowedAdditionalLights;
                    bool expectsGrassGpuInstancing =
                        preset == BenchmarkRenderPreset.GrassGpuInstancing;
                    bool expectsGrassShadowsOff =
                        preset == BenchmarkRenderPreset.GrassShadowCastingOff;
                    int expectedGrassShadowCasterCount =
                        expectsGrassShadowsOff ? 0 : sourceGrassShadowCasterCount;
                    bool expectsGrassRenderingOff =
                        preset == BenchmarkRenderPreset.GrassRenderingOff;
                    int expectedVisibleGrassRendererCount =
                        expectsGrassRenderingOff ? 0 : sourceVisibleGrassRendererCount;

                    if (!Mathf.Approximately(snapshot.RenderScale, expectedScale) ||
                        snapshot.AdditionalLightShadows != expectedAdditionalShadows ||
                        snapshot.ShadowedAdditionalLightCount != expectedShadowedAdditionalLights ||
                        snapshot.SupportsGpuInstancing != SystemInfo.supportsInstancing ||
                        snapshot.GrassGpuInstancing != expectsGrassGpuInstancing ||
                        snapshot.GrassInstancingMaterialCount != sourceGrassMaterials.Length ||
                        snapshot.GrassInstancingRendererCount != sourceGrassRendererCount ||
                        snapshot.GrassShadowCasterRendererCount !=
                            expectedGrassShadowCasterCount ||
                        snapshot.GrassVisibleRendererCount !=
                            expectedVisibleGrassRendererCount ||
                        snapshot.GrassStaticBatchRendererCount !=
                            sourceStaticBatchedGrassRendererCount ||
                        snapshot.GrassCombinedChunkCount !=
                            sourceCombinedGrassChunks.Length ||
                        snapshot.GrassRuntimeRendererCount != sourceGrassRendererCount ||
                        snapshot.GrassSourceRendererCount !=
                            (sourceCombinedGrassChunks.Length > 0
                                ? sourceCombinedGrassRendererCount
                                : sourceGrassRendererCount))
                    {
                        throw new InvalidOperationException(
                            "Render preset " + preset + " applied unexpected settings.");
                    }
                }

                if (QualitySettings.renderPipeline != originalQualityPipeline)
                {
                    throw new InvalidOperationException(
                        "Render preset " + preset + " did not restore the quality pipeline.");
                }

                int restoredShadowedAdditionalLights =
                    UnityEngine.Object.FindObjectsOfType<Light>().Count(light =>
                        light.isActiveAndEnabled &&
                        (light.type == LightType.Point || light.type == LightType.Spot) &&
                        light.shadows != LightShadows.None);
                if (restoredShadowedAdditionalLights != sourceShadowedAdditionalLights)
                {
                    throw new InvalidOperationException(
                        "Render preset " + preset + " did not restore additional-light shadows.");
                }

                foreach (KeyValuePair<Material, bool> sourceState in sourceGrassInstancing)
                {
                    if (sourceState.Key.enableInstancing != sourceState.Value)
                    {
                        throw new InvalidOperationException(
                            "Render preset " + preset +
                            " did not restore grass material instancing.");
                    }
                }

                foreach (KeyValuePair<MeshRenderer, ShadowCastingMode> sourceState in
                    sourceGrassShadowCasting)
                {
                    if (sourceState.Key.shadowCastingMode != sourceState.Value)
                    {
                        throw new InvalidOperationException(
                            "Render preset " + preset +
                            " did not restore grass shadow casting.");
                    }
                }

                foreach (KeyValuePair<MeshRenderer, bool> sourceState in sourceGrassVisibility)
                {
                    if (sourceState.Key.enabled != sourceState.Value)
                    {
                        throw new InvalidOperationException(
                            "Render preset " + preset +
                            " did not restore grass renderer visibility.");
                    }
                }
            }

            Debug.Log(
                "[TreeHouse Benchmark] RenderPresets=PASS, SourceScale=" +
                sourceRenderScale + ", SourceAdditionalShadows=" +
                sourceAdditionalLightShadows + ", SourceShadowedAdditionalLights=" +
                sourceShadowedAdditionalLights + ", GrassMaterials=" +
                sourceGrassMaterials.Length + ", GrassRenderers=" +
                sourceGrassRendererCount + ", GrassShadowCasters=" +
                sourceGrassShadowCasterCount + ", VisibleGrassRenderers=" +
                sourceVisibleGrassRendererCount + ", StaticBatchedGrassRenderers=" +
                sourceStaticBatchedGrassRendererCount + ", CombinedGrassChunks=" +
                sourceCombinedGrassChunks.Length + ", GrassSourceRenderers=" +
                (sourceCombinedGrassChunks.Length > 0
                    ? sourceCombinedGrassRendererCount
                    : sourceGrassRendererCount) + ", GpuInstancingSupport=" +
                SystemInfo.supportsInstancing);
        }

        private static void ValidateWarpRaycastBuffer()
        {
            WarpManager warp = WarpManager.instance;
            FieldInfo hitsField = RequireField(typeof(WarpManager), "_raycastHits");
            MethodInfo raycastMethod = RequireMethod(
                typeof(WarpManager), "RaycastWithoutAllocations");
            RaycastHit[] originalBuffer = (RaycastHit[])hitsField.GetValue(warp);
            GameObject testRoot = new GameObject("TH.WarpRegressionFixtures");

            try
            {
                hitsField.SetValue(warp, new RaycastHit[16]);
                Vector3 rayOrigin = new Vector3(10000f, 10000f, 10000f);
                for (int index = 0; index < 20; index++)
                {
                    GameObject colliderObject = new GameObject("RaycastHit" + index);
                    colliderObject.transform.SetParent(testRoot.transform);
                    colliderObject.transform.position = rayOrigin +
                        Vector3.right * (2f + index * 3f);
                    colliderObject.AddComponent<BoxCollider>();
                }

                Physics.SyncTransforms();
                Ray testRay = new Ray(rayOrigin, Vector3.right);
                int firstHitCount = (int)raycastMethod.Invoke(
                    warp, new object[] { testRay, 100f });
                RaycastHit[] expandedBuffer = (RaycastHit[])hitsField.GetValue(warp);

                if (firstHitCount != 20 || expandedBuffer.Length < 32)
                {
                    throw new InvalidOperationException(
                        "Warp raycast buffer did not preserve all 20 hits. Hits=" +
                        firstHitCount + ", Buffer=" + expandedBuffer.Length + ".");
                }

                int secondHitCount = (int)raycastMethod.Invoke(
                    warp, new object[] { testRay, 100f });
                RaycastHit[] reusedBuffer = (RaycastHit[])hitsField.GetValue(warp);
                if (secondHitCount != 20 || !ReferenceEquals(expandedBuffer, reusedBuffer))
                {
                    throw new InvalidOperationException(
                        "Warp raycast buffer was not reused after expansion.");
                }
            }
            finally
            {
                hitsField.SetValue(warp, originalBuffer);
                UnityEngine.Object.DestroyImmediate(testRoot);
                Physics.SyncTransforms();
            }
        }

        private static void ValidateAudioDistanceFade()
        {
            sfxPlayer player = Resources.FindObjectsOfTypeAll<sfxPlayer>()
                .FirstOrDefault(component => component.gameObject.scene.IsValid() &&
                    component.gameObject.activeInHierarchy && component.enabled);
            if (player == null || player.backgroundSource == null)
            {
                throw new InvalidOperationException(
                    "An active sfxPlayer with an AudioSource was not found.");
            }

            MethodInfo fadeMethod = RequireMethod(typeof(sfxPlayer), "UpdateDistanceFade");
            FieldInfo fadeTimeField = RequireField(typeof(sfxPlayer), "timeForFade");
            FieldInfo managerInstanceField = RequireField(typeof(SfxUpdateManager), "_instance");
            FieldInfo managerPlayersField = RequireField(typeof(SfxUpdateManager), "_players");
            SfxUpdateManager manager =
                (SfxUpdateManager)managerInstanceField.GetValue(null);
            if (manager == null)
            {
                throw new InvalidOperationException("SfxUpdateManager was not created.");
            }

            List<sfxPlayer> managedPlayers =
                (List<sfxPlayer>)managerPlayersField.GetValue(manager);
            Vector3 originalPosition = player.transform.position;
            float originalVolume = player.backgroundSource.volume;
            bool originalMute = player.backgroundSource.mute;
            float originalFadeTime = (float)fadeTimeField.GetValue(player);
            bool originallyEnabled = player.enabled;

            try
            {
                Vector3 listenerPosition = CharacterControl.instance.transform.position;
                player.transform.position = listenerPosition;
                player.backgroundSource.volume = 0f;
                player.backgroundSource.mute = true;
                fadeTimeField.SetValue(player, 0f);

                fadeMethod.Invoke(player, new object[] { listenerPosition, 0.5f });
                fadeMethod.Invoke(player, new object[] { listenerPosition, 0.5f });
                fadeMethod.Invoke(player, new object[] { listenerPosition, 0.5f });
                if (player.backgroundSource.mute ||
                    Mathf.Abs(player.backgroundSource.volume - 0.5f) > 0.0001f)
                {
                    throw new InvalidOperationException(
                        "Near audio fade did not reach the expected unmuted volume.");
                }

                player.transform.position = listenerPosition + Vector3.right * 25f;
                player.backgroundSource.volume = 0.5f;
                fadeMethod.Invoke(player, new object[] { listenerPosition, 0.2f });
                if (Mathf.Abs(player.backgroundSource.volume - 0.2f) > 0.0001f ||
                    Mathf.Abs((float)fadeTimeField.GetValue(player)) > 0.0001f)
                {
                    throw new InvalidOperationException(
                        "Far audio fade did not reduce volume and reset fade time.");
                }

                SfxUpdateManager.Register(player);
                SfxUpdateManager.Register(player);
                if (managedPlayers.Count(candidate => candidate == player) != 1)
                {
                    throw new InvalidOperationException(
                        "SfxUpdateManager registered a duplicate player.");
                }

                player.enabled = false;
                if (managedPlayers.Contains(player))
                {
                    throw new InvalidOperationException(
                        "Disabled sfxPlayer remained registered.");
                }

                player.enabled = true;
                if (managedPlayers.Count(candidate => candidate == player) != 1)
                {
                    throw new InvalidOperationException(
                        "Re-enabled sfxPlayer was not registered exactly once.");
                }
            }
            finally
            {
                player.transform.position = originalPosition;
                player.backgroundSource.volume = originalVolume;
                player.backgroundSource.mute = originalMute;
                fadeTimeField.SetValue(player, originalFadeTime);
                player.enabled = originallyEnabled;
                if (originallyEnabled)
                {
                    SfxUpdateManager.Register(player);
                }
                else
                {
                    SfxUpdateManager.Unregister(player);
                }
            }
        }

        private static void ValidateArrangeColliderInvalidation()
        {
            ArrangeManager arrange = ArrangeManager.instance;
            Camera mainCamera = Camera.main;
            FixedFurniture carpet = arrange.fixedFurnitureList.FirstOrDefault(furniture =>
                (furniture.data.id == 6 || furniture.data.id == 17) &&
                furniture.TryGetComponent(out BoxCollider _));
            if (mainCamera == null || carpet == null ||
                !carpet.TryGetComponent(out BoxCollider carpetCollider))
            {
                throw new InvalidOperationException(
                    "House regression fixture does not contain a camera and carpet collider.");
            }

            MethodInfo disableMethod = RequireMethod(
                typeof(ArrangeManager), "DisableNearCollider");
            MethodInfo invalidateMethod = RequireMethod(
                typeof(ArrangeManager), "InvalidateNearColliderState");
            FieldInfo dirtyField = RequireField(
                typeof(ArrangeManager), "_nearColliderStateDirty");
            Vector3 originalCameraPosition = mainCamera.transform.position;

            try
            {
                if (!(bool)dirtyField.GetValue(arrange))
                {
                    throw new InvalidOperationException(
                        "Loading house furniture did not invalidate collider state.");
                }

                mainCamera.transform.position = carpet.transform.position;
                Physics.SyncTransforms();
                BenchmarkRuntimeCounters.ResetFrame();
                disableMethod.Invoke(arrange, null);
                if (!carpetCollider.isTrigger ||
                    BenchmarkRuntimeCounters.ArrangeCandidatesChecked != 24)
                {
                    throw new InvalidOperationException(
                        "Near carpet check did not scan 24 candidates and enable trigger.");
                }

                BenchmarkRuntimeCounters.ResetFrame();
                disableMethod.Invoke(arrange, null);
                if (BenchmarkRuntimeCounters.ArrangeCandidatesChecked != 0)
                {
                    throw new InvalidOperationException(
                        "Unchanged camera position unexpectedly rescanned furniture.");
                }

                mainCamera.transform.position = carpet.transform.position + Vector3.up * 10f;
                Physics.SyncTransforms();
                BenchmarkRuntimeCounters.ResetFrame();
                disableMethod.Invoke(arrange, null);
                if (carpetCollider.isTrigger ||
                    BenchmarkRuntimeCounters.ArrangeCandidatesChecked != 24)
                {
                    throw new InvalidOperationException(
                        "Moved camera did not rescan 24 candidates and disable trigger.");
                }

                invalidateMethod.Invoke(arrange, null);
                BenchmarkRuntimeCounters.ResetFrame();
                disableMethod.Invoke(arrange, null);
                if (BenchmarkRuntimeCounters.ArrangeCandidatesChecked != 24)
                {
                    throw new InvalidOperationException(
                        "Dirty collider state did not force a furniture rescan.");
                }
            }
            finally
            {
                mainCamera.transform.position = originalCameraPosition;
                Physics.SyncTransforms();
                invalidateMethod.Invoke(arrange, null);
                disableMethod.Invoke(arrange, null);
                BenchmarkRuntimeCounters.ResetFrame();
            }
        }

        private static FieldInfo RequireField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(type.FullName, fieldName);
            }
            return field;
        }

        private static MethodInfo RequireMethod(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }
            return method;
        }

        private static void Finish(int exitCode, string message)
        {
            SessionState.SetInt("TH.Perf.Smoke.ExitCode", exitCode);
            SessionState.SetString("TH.Perf.Smoke.Message", message);
            SessionState.SetString(PhaseKey, "stopping");

            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
            else if (Application.isBatchMode)
            {
                SessionState.EraseBool(ActiveKey);
                EditorApplication.Exit(exitCode);
            }
        }
    }
}
#endif
