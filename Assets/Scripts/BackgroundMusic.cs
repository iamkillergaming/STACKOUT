using UnityEngine;

/// <summary>Attach to a scene object and assign your background music clip.</summary>
[DisallowMultipleComponent]
public sealed class BackgroundMusic : MonoBehaviour
{
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioClip musicClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.5f;
    [SerializeField] private bool playOnStart = true;

    private bool ownsSource;
    public bool IsPlaying => musicSource != null && musicSource.isPlaying;
    public AudioClip CurrentClip => musicSource != null ? musicSource.clip : musicClip;

    private void Start()
    {
        if (playOnStart) Play();
    }

    public void Play() => PlayAt(0f);

    private void PlayAt(float startTime)
    {
        if (musicClip == null) return;
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            ownsSource = true;
        }

        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.loop = true;
        musicSource.volume = volume;
        if (musicSource.isPlaying && musicSource.clip == musicClip) return;
        musicSource.clip = musicClip;
        musicSource.time = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, musicClip.length - .01f));
        musicSource.Play();
    }

    public void Stop()
    {
        if (musicSource != null) musicSource.Stop();
    }

    public void PlayClip(AudioClip clip)
    {
        musicClip = clip;
        if (clip == null) Stop();
        else Play();
    }

    /// <summary>Starts a ready track after its silent lead-in; callers can retry an unfinished load.</summary>
    public bool TryPlayClip(AudioClip clip, float startTime = 0f)
    {
        if (clip == null || !isActiveAndEnabled) return false;
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        if (clip.loadState != AudioDataLoadState.Loaded) return false;
        musicClip = clip;
        PlayAt(startTime);
        return IsPlaying && CurrentClip == clip;
    }

    private void OnValidate()
    {
        if (Application.isPlaying && musicSource != null) musicSource.volume = volume;
    }

    private void OnDisable()
    {
        Stop();
    }

    private void OnDestroy()
    {
        if (ownsSource && musicSource != null) Destroy(musicSource);
    }
}
