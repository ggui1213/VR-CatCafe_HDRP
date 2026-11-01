using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Cat Cafe/Cafe Lofi Playlist")]
public sealed class CafeLofiPlaylist : MonoBehaviour
{
    [SerializeField] private AudioClip[] tracks = new AudioClip[0];
    [SerializeField] private AudioSource primarySource;
    [SerializeField] private AudioSource secondarySource;
    [SerializeField, Range(0f, 1f)] private float volume = 0.22f;
    [SerializeField, Min(0f)] private float crossfadeSeconds = 3f;

    public AudioClip CurrentTrack { get; private set; }

    private void OnEnable()
    {
        if (primarySource == null || secondarySource == null || primarySource == secondarySource)
        {
            Debug.LogError("Cafe Lofi Playlist requires two different AudioSources.", this);
            return;
        }

        Configure(primarySource);
        Configure(secondarySource);
        StartCoroutine(PlayPlaylist());
    }

    private static void Configure(AudioSource source)
    {
        source.Stop();
        source.enabled = true;
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.reverbZoneMix = 0f;
        source.priority = 64;
        source.volume = 0f;
    }

    private int FindTrack(int start)
    {
        if (tracks == null || tracks.Length == 0)
            return -1;

        for (int offset = 0; offset < tracks.Length; offset++)
        {
            int index = (start + offset) % tracks.Length;
            if (tracks[index] != null)
                return index;
        }

        return -1;
    }

    private IEnumerator PlayPlaylist()
    {
        int index = FindTrack(0);
        if (index < 0)
        {
            Debug.LogWarning("Cafe Lofi Playlist has no tracks assigned.", this);
            yield break;
        }

        AudioSource active = primarySource;
        AudioSource incoming = secondarySource;
        active.clip = tracks[index];
        CurrentTrack = active.clip;
        active.Play();
        yield return Fade(null, active, Mathf.Min(2f, active.clip.length * 0.25f));

        while (true)
        {
            int nextIndex = FindTrack(index + 1);
            // A one-track playlist uses native looping without repeated crossfades.
            if (nextIndex == index)
            {
                active.loop = true;
                active.volume = volume;
                yield break;
            }

            float fade = Mathf.Min(crossfadeSeconds,
                active.clip.length * 0.25f, tracks[nextIndex].length * 0.25f);

            while (AudioListener.pause || (active.isPlaying && active.time < active.clip.length - fade))
            {
                active.volume = volume;
                yield return null;
            }

            incoming.clip = tracks[nextIndex];
            incoming.volume = 0f;
            incoming.Play();
            CurrentTrack = incoming.clip;
            yield return Fade(active, incoming, fade);
            active.Stop();
            AudioSource previous = active;
            active = incoming;
            incoming = previous;
            index = nextIndex;
        }
    }

    private IEnumerator Fade(AudioSource outgoing, AudioSource incoming, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!AudioListener.pause)
            {
                elapsed += Time.unscaledDeltaTime;
                float blend = Mathf.Clamp01(elapsed / duration);
                if (outgoing != null)
                    outgoing.volume = volume * (1f - blend);
                incoming.volume = volume * blend;
            }
            yield return null;
        }

        if (outgoing != null)
            outgoing.volume = 0f;
        incoming.volume = volume;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (primarySource != null)
            primarySource.Stop();
        if (secondarySource != null)
            secondarySource.Stop();
        CurrentTrack = null;
    }
}
