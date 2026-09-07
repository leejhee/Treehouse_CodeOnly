#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using StarterAssets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TreeHouse.PerformanceBenchmark
{
    public sealed class PerformanceBenchmarkController : MonoBehaviour
    {
        private const float WarmupSeconds = 60f;
        private const float CaptureSeconds = 120f;
        private const float WaitingPollSeconds = 5f;
        private const float CaptureThermalPollSeconds = 10f;

        private enum BenchmarkState
        {
            Waiting,
            Warmup,
            Capturing,
            Complete,
            Invalid
        }

        private static PerformanceBenchmarkController _instance;

        private readonly AndroidThermalReader _thermalReader = new AndroidThermalReader();
        private readonly BenchmarkMetricsRecorder _recorder = new BenchmarkMetricsRecorder();
        private readonly BenchmarkRenderPresetSession _renderPresetSession =
            new BenchmarkRenderPresetSession();
        private BenchmarkThermalSnapshot _latestThermal = BenchmarkThermalSnapshot.Unavailable;
        private BenchmarkRunMetadata _metadata;
        private BenchmarkState _state = BenchmarkState.Waiting;
        private StarterAssetsInputs _starterInputs;
        private bool _starterInputLookupCompleted;
        private string _referenceTemperatureText;
        private string _temperatureToleranceText;
        private string _ambientTemperatureText;
        private string _pairIdText;
        private string _pairOrder;
        private string _statusMessage = "Waiting for the first device reading.";
        private string _runDirectory;
        private double _stateStartedAt;
        private double _nextThermalPollAt;
        private bool _thermalUpdatedThisFrame;
        private int _previousTargetFrameRate;
        private BenchmarkScenario _selectedScenario;
        private BenchmarkRenderPreset _selectedRenderPreset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateForBenchmarkBuild()
        {
            if (_instance != null)
            {
                return;
            }

            bool isPerformanceBuild = Application.version.StartsWith(
                "perf-", StringComparison.OrdinalIgnoreCase);
            if (!Application.isEditor && (!Debug.isDebugBuild || !isPerformanceBuild))
            {
                return;
            }

            GameObject root = new GameObject("TreeHousePerformanceBenchmark");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<PerformanceBenchmarkController>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _previousTargetFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 5;

            _selectedScenario = (BenchmarkScenario)Mathf.Clamp(
                PlayerPrefs.GetInt("TH.Perf.Scenario", 0),
                (int)BenchmarkScenario.FieldStatic,
                (int)BenchmarkScenario.HouseMixed24Static);
            _selectedRenderPreset = (BenchmarkRenderPreset)Mathf.Clamp(
                PlayerPrefs.GetInt("TH.Perf.RenderPreset", 0),
                (int)BenchmarkRenderPreset.Current,
                (int)BenchmarkRenderPreset.GrassRenderingOff);
            BenchmarkScenarioFixture.PrepareSaveBeforeStart();

            _referenceTemperatureText = PlayerPrefs.GetString("TH.Perf.TRef", string.Empty);
            _temperatureToleranceText = PlayerPrefs.GetString("TH.Perf.Tolerance", "1.0");
            _ambientTemperatureText = PlayerPrefs.GetString("TH.Perf.Ambient", "23.0");
            _pairIdText = PlayerPrefs.GetString("TH.Perf.PairId", "01");
            _pairOrder = PlayerPrefs.GetString("TH.Perf.PairOrder", "AB");
            _nextThermalPollAt = 0.0;
        }

        private void Update()
        {
            switch (_state)
            {
                case BenchmarkState.Waiting:
                    UpdateWaiting();
                    break;
                case BenchmarkState.Warmup:
                    HoldPlayerInputStill();
                    UpdateWarmup();
                    break;
                case BenchmarkState.Capturing:
                    HoldPlayerInputStill();
                    UpdateCapture();
                    break;
            }
        }

        private void LateUpdate()
        {
            if (_state != BenchmarkState.Capturing)
            {
                return;
            }

            _recorder.CaptureFrame(_latestThermal, _thermalUpdatedThisFrame);
            _thermalUpdatedThisFrame = false;
        }

        private void UpdateWaiting()
        {
            if (Time.realtimeSinceStartupAsDouble < _nextThermalPollAt)
            {
                return;
            }

            _nextThermalPollAt = Time.realtimeSinceStartupAsDouble + WaitingPollSeconds;
            _thermalReader.TryRead(false, out _latestThermal);
            RefreshReadyStatus();
        }

        private void UpdateWarmup()
        {
            if (Time.realtimeSinceStartupAsDouble - _stateStartedAt < WarmupSeconds)
            {
                return;
            }

            _thermalReader.TryRead(true, out _latestThermal);
            _metadata.warmupCompletedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            CopyCaptureStartThermalToMetadata();
            _metadata.warmupTemperatureRiseC = SanitizeFloat(
                _latestThermal.BatteryTemperatureC - _metadata.batteryTemperaturePreWarmupC);

            if (!CanBeginCaptureWithCurrentConditions(out string failureReason))
            {
                Invalidate("pre_capture_" + failureReason);
                return;
            }

            _metadata.captureStartedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            _recorder.Start(_runDirectory, _metadata.runId);
            _state = BenchmarkState.Capturing;
            _stateStartedAt = Time.realtimeSinceStartupAsDouble;
            _nextThermalPollAt = _stateStartedAt + CaptureThermalPollSeconds;
            _thermalUpdatedThisFrame = false;
        }

        private void UpdateCapture()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= _nextThermalPollAt)
            {
                _nextThermalPollAt += CaptureThermalPollSeconds;
                _thermalReader.TryRead(true, out _latestThermal);
                _thermalUpdatedThisFrame = true;

                if (!_latestThermal.IsAvailable)
                {
                    Invalidate("capture_thermal_data_unavailable");
                    return;
                }

                if (_latestThermal.IsExternalPowerConnected)
                {
                    Invalidate("capture_external_power_connected_" + _latestThermal.PowerSource);
                    return;
                }
            }

            if (now - _stateStartedAt >= CaptureSeconds)
            {
                FinishCapture(true, string.Empty);
            }
        }

        private void BeginRun()
        {
            _thermalReader.TryRead(true, out _latestThermal);
            if (!CanBeginWarmupWithCurrentConditions(out string failureReason))
            {
                _statusMessage = "WAIT: " + failureReason;
                return;
            }

            if (SceneManager.GetActiveScene().name != "RealIngame")
            {
                _statusMessage = "WAIT: RealIngame scene is not active.";
                return;
            }

            if (!TryParseFloat(_ambientTemperatureText, out float ambientTemperature) ||
                !TryParseFloat(_referenceTemperatureText, out float referenceTemperature) ||
                !TryParseFloat(_temperatureToleranceText, out float tolerance))
            {
                _statusMessage = "WAIT: Enter valid temperature values.";
                return;
            }

            if (!BenchmarkScenarioFixture.TryApply(_selectedScenario, out string scenarioFailure))
            {
                _statusMessage = "WAIT: " + scenarioFailure;
                return;
            }

            if (!_renderPresetSession.TryApply(
                    _selectedRenderPreset,
                    out BenchmarkRenderSnapshot renderSnapshot,
                    out string renderFailure))
            {
                _statusMessage = "WAIT: " + renderFailure;
                return;
            }

            SaveConfiguration();
            _starterInputs = FindObjectOfType<StarterAssetsInputs>();
            HoldPlayerInputStill();

            string variant = GetVariant();
            bool grassStaticBatchingExpected =
                string.Equals(variant, "FPSGB", StringComparison.OrdinalIgnoreCase);
            bool grassMeshCombiningExpected =
                string.Equals(variant, "FPSGC", StringComparison.OrdinalIgnoreCase);
            if (grassStaticBatchingExpected &&
                renderSnapshot.GrassStaticBatchRendererCount == 0)
            {
                _renderPresetSession.Restore();
                _statusMessage =
                    "WAIT: FPSGB build has no statically batched grass renderers";
                return;
            }
            if (string.Equals(variant, "FPS", StringComparison.OrdinalIgnoreCase) &&
                renderSnapshot.GrassStaticBatchRendererCount != 0)
            {
                _renderPresetSession.Restore();
                _statusMessage =
                    "WAIT: FPS baseline unexpectedly contains static-batched grass";
                return;
            }
            if (grassMeshCombiningExpected &&
                (renderSnapshot.GrassCombinedChunkCount == 0 ||
                 renderSnapshot.GrassSourceRendererCount <=
                    renderSnapshot.GrassRuntimeRendererCount))
            {
                _renderPresetSession.Restore();
                _statusMessage =
                    "WAIT: FPSGC build did not replace source grass renderers";
                return;
            }
            if (string.Equals(variant, "FPS", StringComparison.OrdinalIgnoreCase) &&
                renderSnapshot.GrassCombinedChunkCount != 0)
            {
                _renderPresetSession.Restore();
                _statusMessage =
                    "WAIT: FPS baseline unexpectedly contains combined grass chunks";
                return;
            }

            string safePairId = SanitizeIdentifier(_pairIdText);
            string safePairOrder = SanitizeIdentifier(_pairOrder);
            string timestamp = DateTime.Now.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            BenchmarkScenarioDetails scenario = BenchmarkScenarioFixture.GetDetails(_selectedScenario);
            string scenarioToken = _selectedScenario == BenchmarkScenario.FieldStatic ? "FIELD" : "HOUSE24";
            string runId = "TH_" + variant + "_" + scenarioToken + "_" +
                renderSnapshot.PresetId + "_P" + safePairId + "_" + safePairOrder + "_" + timestamp;

            Resolution currentResolution = Screen.currentResolution;

            string runRequestedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            _metadata = new BenchmarkRunMetadata
            {
                runId = runId,
                variant = variant,
                pairId = safePairId,
                pairOrder = safePairOrder,
                scenarioId = scenario.Id,
                scenarioFixtureVersion = BenchmarkScenarioFixture.FixtureVersion,
                deterministicSeed = BenchmarkScenarioFixture.Seed,
                fixtureFieldObjectCount = scenario.FieldObjectCount,
                fixtureHouseFurnitureCount = scenario.HouseFurnitureCount,
                fixtureHouseCarpetCount = scenario.HouseCarpetCount,
                applicationVersion = Application.version,
                applicationIdentifier = Application.identifier,
                unityVersion = Application.unityVersion,
                deviceModel = SystemInfo.deviceModel,
                operatingSystem = SystemInfo.operatingSystem,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                metricsSchemaVersion = "fps-render-diagnostics-v6",
                unityProfilerRawEnabled = false,
                renderPresetId = renderSnapshot.PresetId,
                renderPresetDisplayName = renderSnapshot.PresetDisplayName,
                renderPipelineName = renderSnapshot.PipelineName,
                qualityLevel = renderSnapshot.QualityLevel,
                qualityName = renderSnapshot.QualityName,
                targetFrameRate = 60,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                screenRefreshRateHz = (float)currentResolution.refreshRateRatio.value,
                renderScale = renderSnapshot.RenderScale,
                mainLightShadows = renderSnapshot.MainLightShadows,
                additionalLightShadows = renderSnapshot.AdditionalLightShadows,
                shadowedAdditionalLightCount = renderSnapshot.ShadowedAdditionalLightCount,
                supportsGpuInstancing = renderSnapshot.SupportsGpuInstancing,
                grassGpuInstancing = renderSnapshot.GrassGpuInstancing,
                grassInstancingMaterialCount = renderSnapshot.GrassInstancingMaterialCount,
                grassInstancingRendererCount = renderSnapshot.GrassInstancingRendererCount,
                grassShadowCasterRendererCount =
                    renderSnapshot.GrassShadowCasterRendererCount,
                grassVisibleRendererCount = renderSnapshot.GrassVisibleRendererCount,
                grassStaticBatchingExpected = grassStaticBatchingExpected,
                grassStaticBatchRendererCount =
                    renderSnapshot.GrassStaticBatchRendererCount,
                grassMeshCombiningExpected = grassMeshCombiningExpected,
                grassSourceRendererCount = renderSnapshot.GrassSourceRendererCount,
                grassRuntimeRendererCount = renderSnapshot.GrassRuntimeRendererCount,
                grassCombinedChunkCount = renderSnapshot.GrassCombinedChunkCount,
                runRequestedAtUtc = runRequestedAtUtc,
                warmupStartedAtUtc = runRequestedAtUtc,
                ambientTemperatureC = ambientTemperature,
                referenceTemperatureC = referenceTemperature,
                temperatureToleranceC = tolerance,
                valid = false,
                invalidReason = string.Empty
            };
            CopyPreWarmupThermalToMetadata();
            _runDirectory = Path.Combine(
                Application.persistentDataPath, "PerformanceBenchmark", _metadata.runId);

            Application.targetFrameRate = 60;
            BenchmarkRuntimeCounters.ResetFrame();
            _state = BenchmarkState.Warmup;
            _stateStartedAt = Time.realtimeSinceStartupAsDouble;
            _statusMessage = "WARMUP";
        }

        private void FinishCapture(bool valid, string invalidReason)
        {
            _thermalReader.TryRead(true, out _latestThermal);

            if (valid && !_latestThermal.IsAvailable)
            {
                valid = false;
                invalidReason = "capture_end_thermal_data_unavailable";
            }
            else if (valid && _latestThermal.IsExternalPowerConnected)
            {
                valid = false;
                invalidReason = "capture_end_external_power_connected_" + _latestThermal.PowerSource;
            }

            _metadata.captureCompletedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            _metadata.runCompletedAtUtc = _metadata.captureCompletedAtUtc;
            _metadata.batteryPercentEnd = _latestThermal.BatteryPercent;
            _metadata.batteryTemperatureEndC = SanitizeFloat(_latestThermal.BatteryTemperatureC);
            _metadata.thermalStatusEnd = _latestThermal.ThermalStatus;
            _metadata.thermalHeadroomEnd = SanitizeFloat(_latestThermal.ThermalHeadroom);
            _metadata.batteryPluggedEnd = _latestThermal.BatteryPlugged;
            _metadata.powerSourceEnd = _latestThermal.PowerSource;
            _metadata.externalPowerConnectedEnd = _latestThermal.IsExternalPowerConnected;
            _metadata.valid = valid;
            _metadata.invalidReason = invalidReason;
            _recorder.StopAndWrite(_metadata);
            _renderPresetSession.Restore();

            _state = valid ? BenchmarkState.Complete : BenchmarkState.Invalid;
            _statusMessage = valid
                ? "COMPLETE: " + _metadata.runId
                : "INVALID: " + invalidReason;
            Application.targetFrameRate = 5;
        }

        private void Invalidate(string reason)
        {
            if (_state == BenchmarkState.Capturing)
            {
                FinishCapture(false, reason);
                return;
            }

            _thermalReader.TryRead(true, out _latestThermal);
            _metadata.runCompletedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            _metadata.batteryPercentEnd = _latestThermal.BatteryPercent;
            _metadata.batteryTemperatureEndC = SanitizeFloat(_latestThermal.BatteryTemperatureC);
            _metadata.thermalStatusEnd = _latestThermal.ThermalStatus;
            _metadata.thermalHeadroomEnd = SanitizeFloat(_latestThermal.ThermalHeadroom);
            _metadata.batteryPluggedEnd = _latestThermal.BatteryPlugged;
            _metadata.powerSourceEnd = _latestThermal.PowerSource;
            _metadata.externalPowerConnectedEnd = _latestThermal.IsExternalPowerConnected;
            _metadata.valid = false;
            _metadata.invalidReason = reason;
            _recorder.WriteMetadataOnly(_runDirectory, _metadata.runId, _metadata);
            _renderPresetSession.Restore();

            _state = BenchmarkState.Invalid;
            _statusMessage = "INVALID: " + reason;
            Application.targetFrameRate = 5;
        }

        private bool CanBeginWarmupWithCurrentConditions(out string failureReason)
        {
            if (Application.isEditor)
            {
                failureReason = string.Empty;
                return true;
            }

            if (!_latestThermal.IsAvailable)
            {
                failureReason = "thermal data unavailable";
                return false;
            }

            if (_latestThermal.IsExternalPowerConnected)
            {
                failureReason = "disconnect external power (" + _latestThermal.PowerSource + ")";
                return false;
            }

            if (!TryParseFloat(_referenceTemperatureText, out float referenceTemperature) ||
                !TryParseFloat(_temperatureToleranceText, out float tolerance))
            {
                failureReason = "T_ref or tolerance is not configured";
                return false;
            }

            if (_latestThermal.ThermalStatus != 0)
            {
                failureReason = "thermal status is " + _latestThermal.ThermalStatus;
                return false;
            }

            if (_latestThermal.BatteryPercent < 40f || _latestThermal.BatteryPercent > 80f)
            {
                failureReason = "battery must be between 40% and 80%";
                return false;
            }

            float difference = Mathf.Abs(_latestThermal.BatteryTemperatureC - referenceTemperature);
            if (difference > Mathf.Abs(tolerance))
            {
                failureReason = "battery temperature is outside the allowed range";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        private bool CanBeginCaptureWithCurrentConditions(out string failureReason)
        {
            if (Application.isEditor)
            {
                failureReason = string.Empty;
                return true;
            }

            if (!_latestThermal.IsAvailable)
            {
                failureReason = "thermal data unavailable";
                return false;
            }

            if (_latestThermal.IsExternalPowerConnected)
            {
                failureReason = "external power connected (" + _latestThermal.PowerSource + ")";
                return false;
            }

            if (_latestThermal.ThermalStatus != 0)
            {
                failureReason = "thermal status is " + _latestThermal.ThermalStatus;
                return false;
            }

            if (_latestThermal.BatteryPercent < 40f || _latestThermal.BatteryPercent > 80f)
            {
                failureReason = "battery must be between 40% and 80%";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        private void RefreshReadyStatus()
        {
            _statusMessage = CanBeginWarmupWithCurrentConditions(out string failureReason)
                ? "READY"
                : "WAIT: " + failureReason;
        }

        private void CopyPreWarmupThermalToMetadata()
        {
            _metadata.batteryPercentPreWarmup = _latestThermal.BatteryPercent;
            _metadata.batteryTemperaturePreWarmupC = SanitizeFloat(_latestThermal.BatteryTemperatureC);
            _metadata.thermalStatusPreWarmup = _latestThermal.ThermalStatus;
            _metadata.thermalHeadroomPreWarmup = SanitizeFloat(_latestThermal.ThermalHeadroom);
            _metadata.batteryPluggedPreWarmup = _latestThermal.BatteryPlugged;
            _metadata.powerSourcePreWarmup = _latestThermal.PowerSource;
            _metadata.externalPowerConnectedPreWarmup = _latestThermal.IsExternalPowerConnected;
        }

        private void CopyCaptureStartThermalToMetadata()
        {
            _metadata.batteryPercentStart = _latestThermal.BatteryPercent;
            _metadata.batteryTemperatureStartC = SanitizeFloat(_latestThermal.BatteryTemperatureC);
            _metadata.thermalStatusStart = _latestThermal.ThermalStatus;
            _metadata.thermalHeadroomStart = SanitizeFloat(_latestThermal.ThermalHeadroom);
            _metadata.batteryPluggedStart = _latestThermal.BatteryPlugged;
            _metadata.powerSourceStart = _latestThermal.PowerSource;
            _metadata.externalPowerConnectedStart = _latestThermal.IsExternalPowerConnected;
        }

        private void HoldPlayerInputStill()
        {
            if (!_starterInputLookupCompleted)
            {
                _starterInputs = FindObjectOfType<StarterAssetsInputs>();
                _starterInputLookupCompleted = true;
            }

            if (_starterInputs != null)
            {
                _starterInputs.move = Vector2.zero;
                _starterInputs.look = Vector2.zero;
                _starterInputs.jump = false;
                _starterInputs.sprint = false;
            }

            if (CharacterControl.instance != null)
            {
                CharacterControl.instance.RunAxis = Vector2.zero;
                CharacterControl.instance.LookAxis = Vector2.zero;
            }

            JoyPadHandler.velocity = Vector2.zero;
        }

        private void SaveConfiguration()
        {
            PlayerPrefs.SetString("TH.Perf.TRef", _referenceTemperatureText);
            PlayerPrefs.SetString("TH.Perf.Tolerance", _temperatureToleranceText);
            PlayerPrefs.SetString("TH.Perf.Ambient", _ambientTemperatureText);
            PlayerPrefs.SetString("TH.Perf.PairId", _pairIdText);
            PlayerPrefs.SetString("TH.Perf.PairOrder", _pairOrder);
            PlayerPrefs.SetInt("TH.Perf.Scenario", (int)_selectedScenario);
            PlayerPrefs.SetInt("TH.Perf.RenderPreset", (int)_selectedRenderPreset);
            PlayerPrefs.Save();
        }

        private string GetVariant()
        {
            if (Application.version.StartsWith("perf-FPSGC-", StringComparison.OrdinalIgnoreCase))
            {
                return "FPSGC";
            }

            if (Application.version.StartsWith("perf-FPSGB-", StringComparison.OrdinalIgnoreCase))
            {
                return "FPSGB";
            }

            if (Application.version.StartsWith("perf-A-", StringComparison.OrdinalIgnoreCase))
            {
                return "A";
            }

            if (Application.version.StartsWith("perf-B-", StringComparison.OrdinalIgnoreCase))
            {
                return "B";
            }

            if (Application.version.StartsWith("perf-FPS-", StringComparison.OrdinalIgnoreCase))
            {
                return "FPS";
            }

            return Application.isEditor ? "EDITOR" : "UNKNOWN";
        }

        private void OnGUI()
        {
            if (_state == BenchmarkState.Warmup || _state == BenchmarkState.Capturing)
            {
                return;
            }

            const float panelWidth = 720f;
            const float panelHeight = 1190f;
            const float panelMargin = 24f;

            Rect safeArea = Screen.safeArea;
            float desiredScale = Mathf.Max(1f, Screen.width / 1080f);
            float fitWidthScale = safeArea.width / (panelWidth + (panelMargin * 2f));
            float fitHeightScale = safeArea.height / (panelHeight + (panelMargin * 2f));
            float scale = Mathf.Max(
                0.75f,
                Mathf.Min(desiredScale, fitWidthScale, fitHeightScale));

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            int previousLabelFontSize = GUI.skin.label.fontSize;
            int previousTextFieldFontSize = GUI.skin.textField.fontSize;
            int previousButtonFontSize = GUI.skin.button.fontSize;
            GUI.skin.label.fontSize = Mathf.Max(previousLabelFontSize, 18);
            GUI.skin.textField.fontSize = Mathf.Max(previousTextFieldFontSize, 18);
            GUI.skin.button.fontSize = Mathf.Max(previousButtonFontSize, 18);

            // Screen.safeArea uses a bottom-left origin while IMGUI uses a top-left
            // origin. Converting both axes keeps this panel clear of display cutouts
            // (for example, the Galaxy S23 punch-hole on the left in landscape).
            float safeLeft = safeArea.xMin / scale;
            float safeTop = (Screen.height - safeArea.yMax) / scale;
            GUILayout.BeginArea(
                new Rect(
                    safeLeft + panelMargin,
                    safeTop + panelMargin,
                    panelWidth,
                    panelHeight),
                GUI.skin.box);
            GUILayout.Label("TreeHouse Performance Benchmark");
            GUILayout.Label("Build: " + GetVariant() + " / " + Application.version);
            GUILayout.Label("Scene: " + SceneManager.GetActiveScene().name);
            GUILayout.Space(8f);

            GUILayout.Label("T_ref (C)");
            _referenceTemperatureText = GUILayout.TextField(_referenceTemperatureText ?? string.Empty);
            GUILayout.Label("Tolerance (+/- C)");
            _temperatureToleranceText = GUILayout.TextField(_temperatureToleranceText ?? string.Empty);
            GUILayout.Label("Ambient temperature (C)");
            _ambientTemperatureText = GUILayout.TextField(_ambientTemperatureText ?? string.Empty);
            GUILayout.Label("Pair ID");
            _pairIdText = GUILayout.TextField(_pairIdText ?? string.Empty);
            GUILayout.Label("Pair order");
            _pairOrder = GUILayout.TextField(_pairOrder ?? string.Empty);

            GUILayout.Space(8f);
            GUILayout.Label("Scenario");
            BenchmarkScenarioDetails scenario = BenchmarkScenarioFixture.GetDetails(_selectedScenario);
            GUILayout.Label(scenario.DisplayName);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Field static", GUILayout.Height(48f)))
            {
                _selectedScenario = BenchmarkScenario.FieldStatic;
            }
            if (GUILayout.Button("House / 24", GUILayout.Height(48f)))
            {
                _selectedScenario = BenchmarkScenario.HouseMixed24Static;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Render diagnostic preset");
            GUILayout.Label(BenchmarkRenderPresetSession.GetDisplayName(_selectedRenderPreset));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("R0 Current", GUILayout.Height(44f)))
            {
                _selectedRenderPreset = BenchmarkRenderPreset.Current;
            }
            if (GUILayout.Button("R1 No add shadows", GUILayout.Height(44f)))
            {
                _selectedRenderPreset = BenchmarkRenderPreset.NoAdditionalLightShadows;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("R2 Scale 0.8", GUILayout.Height(44f)))
            {
                _selectedRenderPreset = BenchmarkRenderPreset.RenderScale80;
            }
            if (GUILayout.Button("R3 Combined", GUILayout.Height(44f)))
            {
                _selectedRenderPreset =
                    BenchmarkRenderPreset.NoAdditionalLightShadowsRenderScale80;
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("R4 Grass GPU Instancing", GUILayout.Height(44f)))
            {
                _selectedRenderPreset = BenchmarkRenderPreset.GrassGpuInstancing;
            }
            if (GUILayout.Button("R5 Grass Shadows Off", GUILayout.Height(44f)))
            {
                _selectedRenderPreset = BenchmarkRenderPreset.GrassShadowCastingOff;
            }
            if (GUILayout.Button("R6 Grass Rendering Off (diagnostic)", GUILayout.Height(44f)))
            {
                _selectedRenderPreset = BenchmarkRenderPreset.GrassRenderingOff;
            }

            GUILayout.Space(8f);
            GUILayout.Label("Battery: " + FormatFloat(_latestThermal.BatteryPercent, "F0") + "%");
            GUILayout.Label("Battery temperature: " + FormatFloat(_latestThermal.BatteryTemperatureC, "F1") + " C");
            GUILayout.Label("Thermal status: " + _latestThermal.ThermalStatus);
            GUILayout.Label("Power: " + _latestThermal.PowerSource);
            GUILayout.Label(_statusMessage);

            GUILayout.Space(12f);
            if (_state == BenchmarkState.Waiting && GUILayout.Button("Start: 60s warmup + 120s capture", GUILayout.Height(64f)))
            {
                BeginRun();
            }

            if (_state == BenchmarkState.Complete || _state == BenchmarkState.Invalid)
            {
                GUILayout.Label("Logs: " + Path.Combine(Application.persistentDataPath, "PerformanceBenchmark"));
                if (GUILayout.Button("Return to waiting", GUILayout.Height(52f)))
                {
                    _state = BenchmarkState.Waiting;
                    _nextThermalPollAt = 0.0;
                }
            }

            GUILayout.EndArea();
            GUI.skin.label.fontSize = previousLabelFontSize;
            GUI.skin.textField.fontSize = previousTextFieldFontSize;
            GUI.skin.button.fontSize = previousButtonFontSize;
            GUI.matrix = previousMatrix;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && (_state == BenchmarkState.Warmup || _state == BenchmarkState.Capturing))
            {
                Invalidate("app_focus_lost");
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && (_state == BenchmarkState.Warmup || _state == BenchmarkState.Capturing))
            {
                Invalidate("app_paused");
            }
        }

        private void OnDestroy()
        {
            _recorder.Dispose();
            _renderPresetSession.Dispose();
            _thermalReader.Dispose();
            Application.targetFrameRate = _previousTargetFrameRate;

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private static bool TryParseFloat(string value, out float parsed)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace(',', '.');
            return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UNSET";
            }

            char[] characters = value.Trim().ToUpperInvariant().ToCharArray();
            for (int index = 0; index < characters.Length; index++)
            {
                char character = characters[index];
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_')
                {
                    characters[index] = '_';
                }
            }

            return new string(characters);
        }

        private static float SanitizeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? -1f : value;
        }

        private static string FormatFloat(float value, string format)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? "N/A"
                : value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
#endif
