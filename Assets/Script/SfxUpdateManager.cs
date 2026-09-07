using System.Collections.Generic;
using UnityEngine;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using TreeHouse.PerformanceBenchmark;
using Unity.Profiling;
#endif

public sealed class SfxUpdateManager : MonoBehaviour
{
    private const int UpdatesPerFrame = 4;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    private static readonly ProfilerMarker DistanceFadeMarker =
        new ProfilerMarker("TH.Audio.DistanceFade");
#endif

    private static SfxUpdateManager _instance;
    private readonly List<sfxPlayer> _players = new List<sfxPlayer>(16);
    private readonly List<double> _lastUpdateTimes = new List<double>(16);
    private int _nextPlayerIndex;

    public static void Register(sfxPlayer player)
    {
        EnsureInstance();
        if (!_instance._players.Contains(player))
        {
            _instance._players.Add(player);
            _instance._lastUpdateTimes.Add(Time.timeAsDouble - Time.deltaTime);
        }
    }

    public static void Unregister(sfxPlayer player)
    {
        if (_instance != null)
        {
            int playerIndex = _instance._players.IndexOf(player);
            if (playerIndex >= 0)
            {
                _instance.RemoveAt(playerIndex);
            }
        }
    }

    private static void EnsureInstance()
    {
        if (_instance != null)
        {
            return;
        }

        GameObject managerObject = new GameObject(nameof(SfxUpdateManager));
        DontDestroyOnLoad(managerObject);
        _instance = managerObject.AddComponent<SfxUpdateManager>();
    }

    private void Update()
    {
        if (_players.Count == 0 || CharacterControl.instance == null)
        {
            return;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        using (DistanceFadeMarker.Auto())
#endif
        {
            Vector3 listenerPosition = CharacterControl.instance.transform.position;
            double currentTime = Time.timeAsDouble;
            int updatesRemaining = Mathf.Min(UpdatesPerFrame, _players.Count);

            while (updatesRemaining > 0 && _players.Count > 0)
            {
                if (_nextPlayerIndex >= _players.Count)
                {
                    _nextPlayerIndex = 0;
                }

                int playerIndex = _nextPlayerIndex;
                sfxPlayer player = _players[playerIndex];
                if (player == null)
                {
                    RemoveAt(playerIndex);
                    continue;
                }

                _nextPlayerIndex++;
                updatesRemaining--;

                if (player.isActiveAndEnabled)
                {
                    float elapsedTime = Mathf.Max(
                        0f,
                        (float)(currentTime - _lastUpdateTimes[playerIndex]));
                    _lastUpdateTimes[playerIndex] = currentTime;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    BenchmarkRuntimeCounters.AudioUpdateCalls++;
#endif
                    player.UpdateDistanceFade(listenerPosition, elapsedTime);
                }
            }
        }
    }

    private void RemoveAt(int playerIndex)
    {
        _players.RemoveAt(playerIndex);
        _lastUpdateTimes.RemoveAt(playerIndex);

        if (_players.Count == 0)
        {
            _nextPlayerIndex = 0;
        }
        else if (playerIndex < _nextPlayerIndex)
        {
            _nextPlayerIndex--;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }
}
