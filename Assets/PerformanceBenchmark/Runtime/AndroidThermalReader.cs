#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;

namespace TreeHouse.PerformanceBenchmark
{
    public struct BenchmarkThermalSnapshot
    {
        public bool IsAvailable;
        public float BatteryPercent;
        public float BatteryTemperatureC;
        public int ThermalStatus;
        public float ThermalHeadroom;
        public int BatteryPlugged;

        public bool IsExternalPowerConnected
        {
            get { return BatteryPlugged > 0; }
        }

        public string PowerSource
        {
            get
            {
                if (BatteryPlugged < 0)
                {
                    return "UNKNOWN";
                }
                if (BatteryPlugged == 0)
                {
                    return "BATTERY";
                }
                if ((BatteryPlugged & 2) != 0)
                {
                    return "USB";
                }
                if ((BatteryPlugged & 1) != 0)
                {
                    return "AC";
                }
                if ((BatteryPlugged & 4) != 0)
                {
                    return "WIRELESS";
                }
                if ((BatteryPlugged & 8) != 0)
                {
                    return "DOCK";
                }
                return "EXTERNAL(" + BatteryPlugged + ")";
            }
        }

        public static BenchmarkThermalSnapshot Unavailable
        {
            get
            {
                return new BenchmarkThermalSnapshot
                {
                    IsAvailable = false,
                    BatteryPercent = -1f,
                    BatteryTemperatureC = float.NaN,
                    ThermalStatus = -1,
                    ThermalHeadroom = float.NaN,
                    BatteryPlugged = -1
                };
            }
        }
    }

    /// <summary>
    /// Reads Android battery and thermal information without a separate native app.
    /// Android Java objects are cached; callers should still sample infrequently.
    /// </summary>
    public sealed class AndroidThermalReader : IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _activity;
        private AndroidJavaObject _powerManager;
        private AndroidJavaObject _batteryFilter;
        private bool _initialized;
#endif

        public bool TryRead(bool includeHeadroom, out BenchmarkThermalSnapshot snapshot)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                EnsureInitialized();

                using (AndroidJavaObject batteryIntent = _activity.Call<AndroidJavaObject>(
                           "registerReceiver", (AndroidJavaObject)null, _batteryFilter))
                {
                    if (batteryIntent == null)
                    {
                        snapshot = BenchmarkThermalSnapshot.Unavailable;
                        return false;
                    }

                    int temperatureTenths = batteryIntent.Call<int>("getIntExtra", "temperature", -1);
                    int level = batteryIntent.Call<int>("getIntExtra", "level", -1);
                    int scale = batteryIntent.Call<int>("getIntExtra", "scale", -1);
                    int plugged = batteryIntent.Call<int>("getIntExtra", "plugged", -1);
                    int thermalStatus = _powerManager.Call<int>("getCurrentThermalStatus");
                    float thermalHeadroom = float.NaN;

                    if (includeHeadroom)
                    {
                        try
                        {
                            thermalHeadroom = _powerManager.Call<float>("getThermalHeadroom", 0);
                        }
                        catch (Exception)
                        {
                            thermalHeadroom = float.NaN;
                        }
                    }

                    snapshot = new BenchmarkThermalSnapshot
                    {
                        IsAvailable = temperatureTenths >= 0 && level >= 0 && scale > 0 && plugged >= 0,
                        BatteryPercent = scale > 0 ? level * 100f / scale : -1f,
                        BatteryTemperatureC = temperatureTenths >= 0 ? temperatureTenths / 10f : float.NaN,
                        ThermalStatus = thermalStatus,
                        ThermalHeadroom = thermalHeadroom,
                        BatteryPlugged = plugged
                    };
                    return snapshot.IsAvailable;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[TreeHouse Benchmark] Android thermal read failed: " + exception.Message);
                snapshot = BenchmarkThermalSnapshot.Unavailable;
                return false;
            }
#else
            snapshot = BenchmarkThermalSnapshot.Unavailable;
            snapshot.BatteryPercent = SystemInfo.batteryLevel >= 0f ? SystemInfo.batteryLevel * 100f : -1f;
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }

            _powerManager = _activity.Call<AndroidJavaObject>("getSystemService", "power");
            _batteryFilter = new AndroidJavaObject(
                "android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED");
            _initialized = true;
        }
#endif

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_batteryFilter != null)
            {
                _batteryFilter.Dispose();
                _batteryFilter = null;
            }

            if (_powerManager != null)
            {
                _powerManager.Dispose();
                _powerManager = null;
            }

            if (_activity != null)
            {
                _activity.Dispose();
                _activity = null;
            }

            _initialized = false;
#endif
        }
    }
}
#endif
