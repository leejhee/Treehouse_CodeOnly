#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace TreeHouse.PerformanceBenchmark
{
    /// <summary>
    /// Collects frame data into a preallocated array. Disk serialization happens only
    /// after the profiler has stopped so the recorder does not pollute capture frames.
    /// </summary>
    public sealed class BenchmarkMetricsRecorder : IDisposable
    {
        private const int MaxSamples = 60 * 130;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private struct FrameSample
        {
            public int FrameIndex;
            public double ElapsedMs;
            public double CpuFrameMs;
            public double CpuMainThreadMs;
            public double CpuRenderThreadMs;
            public double GpuFrameMs;
            public double FrameIntervalMs;
            public long GcAllocatedBytes;
            public long BatchesCount;
            public long DrawCallsCount;
            public long SetPassCallsCount;
            public long TrianglesCount;
            public long VerticesCount;
            public double WarpRaycastMs;
            public double AudioDistanceFadeMs;
            public double ArrangeDisableNearColliderMs;
            public int AudioUpdateCalls;
            public int ArrangeCandidatesChecked;
            public int ThermalStatus;
            public float ThermalHeadroom;
            public float BatteryTemperatureC;
            public byte ThermalTelemetryUpdated;
        }

        private readonly FrameSample[] _samples = new FrameSample[MaxSamples];
        private readonly FrameTiming[] _frameTimingBuffer = new FrameTiming[1];
        private ProfilerRecorder _gcAllocatedRecorder;
        private ProfilerRecorder _warpRaycastRecorder;
        private ProfilerRecorder _audioDistanceFadeRecorder;
        private ProfilerRecorder _arrangeDisableNearColliderRecorder;
        private ProfilerRecorder _batchesRecorder;
        private ProfilerRecorder _drawCallsRecorder;
        private ProfilerRecorder _setPassCallsRecorder;
        private ProfilerRecorder _trianglesRecorder;
        private ProfilerRecorder _verticesRecorder;
        private string _runDirectory;
        private string _runId;
        private double _captureStartTime;
        private int _sampleCount;
        private bool _recording;

        public int SampleCount
        {
            get { return _sampleCount; }
        }

        public void Start(string runDirectory, string runId)
        {
            if (_recording)
            {
                throw new InvalidOperationException("Benchmark recorder is already running.");
            }

            _runDirectory = runDirectory;
            _runId = runId;
            Directory.CreateDirectory(_runDirectory);
            _sampleCount = 0;
            _captureStartTime = Time.realtimeSinceStartupAsDouble;
            BenchmarkRuntimeCounters.ResetFrame();

            _gcAllocatedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            _warpRaycastRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts, "TH.Warp.Raycast", 1);
            _audioDistanceFadeRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts, "TH.Audio.DistanceFade", 1);
            _arrangeDisableNearColliderRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts, "TH.Arrange.DisableNearCollider", 1);
            _batchesRecorder = StartOptionalRecorder(ProfilerCategory.Render, "Batches Count");
            _drawCallsRecorder = StartOptionalRecorder(ProfilerCategory.Render, "Draw Calls Count");
            _setPassCallsRecorder = StartOptionalRecorder(ProfilerCategory.Render, "SetPass Calls Count");
            _trianglesRecorder = StartOptionalRecorder(ProfilerCategory.Render, "Triangles Count");
            _verticesRecorder = StartOptionalRecorder(ProfilerCategory.Render, "Vertices Count");

            _recording = true;
        }

        public void CaptureFrame(BenchmarkThermalSnapshot thermal, bool thermalTelemetryUpdated)
        {
            if (!_recording || _sampleCount >= _samples.Length)
            {
                BenchmarkRuntimeCounters.ResetFrame();
                return;
            }

            FrameTimingManager.CaptureFrameTimings();
            uint timingCount = FrameTimingManager.GetLatestTimings(1, _frameTimingBuffer);
            FrameTiming timing = timingCount > 0 ? _frameTimingBuffer[0] : default(FrameTiming);

            FrameSample sample = new FrameSample
            {
                FrameIndex = Time.frameCount,
                ElapsedMs = (Time.realtimeSinceStartupAsDouble - _captureStartTime) * 1000.0,
                CpuFrameMs = timing.cpuFrameTime,
                CpuMainThreadMs = timing.cpuMainThreadFrameTime,
                CpuRenderThreadMs = timing.cpuRenderThreadFrameTime,
                GpuFrameMs = timing.gpuFrameTime,
                FrameIntervalMs = Time.unscaledDeltaTime * 1000.0,
                GcAllocatedBytes = _gcAllocatedRecorder.Valid ? _gcAllocatedRecorder.LastValue : -1,
                BatchesCount = ReadCounter(_batchesRecorder),
                DrawCallsCount = ReadCounter(_drawCallsRecorder),
                SetPassCallsCount = ReadCounter(_setPassCallsRecorder),
                TrianglesCount = ReadCounter(_trianglesRecorder),
                VerticesCount = ReadCounter(_verticesRecorder),
                WarpRaycastMs = ReadMarkerMilliseconds(_warpRaycastRecorder),
                AudioDistanceFadeMs = ReadMarkerMilliseconds(_audioDistanceFadeRecorder),
                ArrangeDisableNearColliderMs = ReadMarkerMilliseconds(_arrangeDisableNearColliderRecorder),
                AudioUpdateCalls = BenchmarkRuntimeCounters.AudioUpdateCalls,
                ArrangeCandidatesChecked = BenchmarkRuntimeCounters.ArrangeCandidatesChecked,
                ThermalStatus = thermal.ThermalStatus,
                ThermalHeadroom = SanitizeFloat(thermal.ThermalHeadroom),
                BatteryTemperatureC = SanitizeFloat(thermal.BatteryTemperatureC),
                ThermalTelemetryUpdated = thermalTelemetryUpdated ? (byte)1 : (byte)0
            };

            _samples[_sampleCount] = sample;
            _sampleCount++;
            BenchmarkRuntimeCounters.ResetFrame();
        }

        public void StopAndWrite(BenchmarkRunMetadata metadata)
        {
            StopRecorders();
            WriteArtifacts(metadata);
        }

        public void WriteMetadataOnly(
            string runDirectory,
            string runId,
            BenchmarkRunMetadata metadata)
        {
            if (_recording)
            {
                throw new InvalidOperationException(
                    "Cannot write metadata-only artifacts while recording.");
            }

            _runDirectory = runDirectory;
            _runId = runId;
            _sampleCount = 0;
            WriteArtifacts(metadata);
        }

        private void WriteArtifacts(BenchmarkRunMetadata metadata)
        {
            Directory.CreateDirectory(_runDirectory);

            metadata.capturedFrames = _sampleCount;
            File.WriteAllText(
                Path.Combine(_runDirectory, _runId + ".metadata.json"),
                JsonUtility.ToJson(metadata, true),
                Encoding.UTF8);

            StringBuilder csv = new StringBuilder(1024 + _sampleCount * 160);
            csv.AppendLine(
                "run_id,frame_index,elapsed_ms,cpu_frame_ms,cpu_main_thread_ms,cpu_render_thread_ms," +
                "gpu_frame_ms,frame_interval_ms,gc_allocated_bytes,batches_count,draw_calls_count," +
                "setpass_calls_count,triangles_count,vertices_count,warp_raycast_ms,audio_distance_fade_ms," +
                "arrange_disable_near_collider_ms,audio_update_calls,arrange_candidates_checked," +
                "thermal_status,thermal_headroom,battery_temperature_c,thermal_telemetry_updated");

            for (int index = 0; index < _sampleCount; index++)
            {
                FrameSample sample = _samples[index];
                csv.Append(_runId).Append(',')
                    .Append(sample.FrameIndex).Append(',')
                    .Append(sample.ElapsedMs.ToString("F3", Invariant)).Append(',')
                    .Append(sample.CpuFrameMs.ToString("F4", Invariant)).Append(',')
                    .Append(sample.CpuMainThreadMs.ToString("F4", Invariant)).Append(',')
                    .Append(sample.CpuRenderThreadMs.ToString("F4", Invariant)).Append(',')
                    .Append(sample.GpuFrameMs.ToString("F4", Invariant)).Append(',')
                    .Append(sample.FrameIntervalMs.ToString("F4", Invariant)).Append(',')
                    .Append(sample.GcAllocatedBytes).Append(',')
                    .Append(sample.BatchesCount).Append(',')
                    .Append(sample.DrawCallsCount).Append(',')
                    .Append(sample.SetPassCallsCount).Append(',')
                    .Append(sample.TrianglesCount).Append(',')
                    .Append(sample.VerticesCount).Append(',')
                    .Append(sample.WarpRaycastMs.ToString("F6", Invariant)).Append(',')
                    .Append(sample.AudioDistanceFadeMs.ToString("F6", Invariant)).Append(',')
                    .Append(sample.ArrangeDisableNearColliderMs.ToString("F6", Invariant)).Append(',')
                    .Append(sample.AudioUpdateCalls).Append(',')
                    .Append(sample.ArrangeCandidatesChecked).Append(',')
                    .Append(sample.ThermalStatus).Append(',')
                    .Append(sample.ThermalHeadroom.ToString("F4", Invariant)).Append(',')
                    .Append(sample.BatteryTemperatureC.ToString("F1", Invariant)).Append(',')
                    .Append(sample.ThermalTelemetryUpdated)
                    .AppendLine();
            }

            File.WriteAllText(
                Path.Combine(_runDirectory, _runId + ".csv"),
                csv.ToString(),
                Encoding.UTF8);
        }

        private void StopRecorders()
        {
            if (!_recording)
            {
                return;
            }

            _recording = false;

            DisposeRecorder(ref _gcAllocatedRecorder);
            DisposeRecorder(ref _warpRaycastRecorder);
            DisposeRecorder(ref _audioDistanceFadeRecorder);
            DisposeRecorder(ref _arrangeDisableNearColliderRecorder);
            DisposeRecorder(ref _batchesRecorder);
            DisposeRecorder(ref _drawCallsRecorder);
            DisposeRecorder(ref _setPassCallsRecorder);
            DisposeRecorder(ref _trianglesRecorder);
            DisposeRecorder(ref _verticesRecorder);
        }

        private static ProfilerRecorder StartOptionalRecorder(
            ProfilerCategory category,
            string statisticName)
        {
            try
            {
                return ProfilerRecorder.StartNew(category, statisticName, 1);
            }
            catch (ArgumentException)
            {
                return default(ProfilerRecorder);
            }
        }

        private static long ReadCounter(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : -1;
        }

        private static double ReadMarkerMilliseconds(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue / 1000000.0 : -1.0;
        }

        private static void DisposeRecorder(ref ProfilerRecorder recorder)
        {
            if (recorder.Valid)
            {
                recorder.Dispose();
            }

            recorder = default(ProfilerRecorder);
        }

        private static float SanitizeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? -1f : value;
        }

        public void Dispose()
        {
            StopRecorders();
        }
    }
}
#endif
