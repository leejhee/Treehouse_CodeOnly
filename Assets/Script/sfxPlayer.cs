using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class sfxPlayer : MonoBehaviour
{


    public AudioSource backgroundSource;
    public AudioClip birdOrWindorBug;
    float distance;
    float timeForFade = 0f;
    bool _started;
    
    void Start()
    {
        backgroundSource = GetComponent<AudioSource>();
        backgroundSource.playOnAwake = true;
        backgroundSource.mute = true;
        _started = true;
        SfxUpdateManager.Register(this);
    }

    void OnEnable()
    {
        if (_started)
        {
            SfxUpdateManager.Register(this);
        }
    }

    void OnDisable()
    {
        if (_started)
        {
            SfxUpdateManager.Unregister(this);
        }
    }

    internal void UpdateDistanceFade(Vector3 listenerPosition, float deltaTime)
    {
        distance = Vector3.Distance(transform.position, listenerPosition);
        if (distance > 20f)
        {
            backgroundSource.volume -= deltaTime * 1.5f;
            timeForFade = 0f;
        }
        else
        {
            backgroundSource.mute = false;
            backgroundSource.volume = Mathf.Lerp(0f, 0.5f, timeForFade);
            if (timeForFade >= 1f)
            {
                timeForFade = 1f;
            }
            else
            {
                timeForFade += 1.5f * deltaTime;
            }
        }
    }
}
