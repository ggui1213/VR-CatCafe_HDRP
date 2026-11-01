using UnityEngine;

/// <summary>
/// The cat's "brain" for petting: accumulates happiness from strokes reported by PettingHand,
/// drives a procedural purr, gentle meows, and posture/animation reactions.
/// Animator states expected (see CatPetting.controller):
/// Sit, Yes, MeowSit, SitToLie, Lie, MeowLie, LieToSit, Sleep
/// </summary>
public class PettableCat : MonoBehaviour
{
    // Roaming owns posture; petting mood, purring and voice remain active.
    public bool MotionControlled { get; set; }

    [Header("References")]
    public Animator animator;
    public AudioSource purrSource;
    public AudioSource voiceSource;
    public AudioClip[] gentleMeows;

    [Header("Mood")]
    [Range(0f, 1f)] public float happiness = 0.2f;
    [Tooltip("Happiness gained per second of ideal petting.")]
    public float happinessGain = 0.12f;
    public float happinessDecay = 0.015f;
    [Tooltip("Stroke speed (m/s) the cat likes most. Slower = less effective, much faster = less pleasant.")]
    public float idealStrokeSpeed = 0.25f;

    [Header("Purr")]
    public float purrMaxVolume = 0.8f;
    [Tooltip("Seconds the cat keeps purring after you stop petting.")]
    public float purrLinger = 3f;
    [Min(0f)] public float pettingTimeBeforePurr = 2.5f;
    public float PettingProgress => Mathf.Clamp01(_continuousPetTime / Mathf.Max(0.01f, pettingTimeBeforePurr));
    public bool IsStationary => !_roaming || !_roaming.isActiveAndEnabled || _roaming.IsStationary;

    [Header("Behaviour timing")]
    public float lieDownHappiness = 0.65f;
    public float minSitTimeBeforeLie = 6f;
    public float sleepAfterIdle = 10f;
    public float sitUpAfterIdle = 14f;
    public float meowCooldown = 12f;

    /// <summary>0..1 purr strength, read by PettingHand for the haptic rumble.</summary>
    public float PurrLevel { get; private set; }
    public bool IsBeingPetted => Time.time - _lastPetTime < 0.35f;

    enum Posture { Sit, Lie, Sleep }
    Posture _posture = Posture.Sit;
    float _postureSince, _lastPetTime = -100f, _busyUntil, _lastMeow = -100f;
    float _frameIntensity;
    float _lastHappinessMark;
    CatIndoorRoaming _roaming;
    float _continuousPetTime;
    bool _purrEarned;
    float _foodInterestUntil, _foodPurr;

    public void ReportFoodInterest(float strength)
    {
        _foodInterestUntil = Time.time + 0.35f;
        _foodPurr = Mathf.Clamp01(strength);
    }

    static readonly int S_Sit = Animator.StringToHash("Sit");
    static readonly int S_Yes = Animator.StringToHash("Yes");
    static readonly int S_MeowSit = Animator.StringToHash("MeowSit");
    static readonly int S_SitToLie = Animator.StringToHash("SitToLie");
    static readonly int S_Lie = Animator.StringToHash("Lie");
    static readonly int S_MeowLie = Animator.StringToHash("MeowLie");
    static readonly int S_LieToSit = Animator.StringToHash("LieToSit");
    static readonly int S_Sleep = Animator.StringToHash("Sleep");

    void Awake()
    {
        _roaming = GetComponent<CatIndoorRoaming>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (purrSource)
        {
            if (!purrSource.clip) purrSource.clip = CreatePurrClip();
            purrSource.loop = true;
            purrSource.volume = 0f;
            purrSource.playOnAwake = false;
        }
    }

    void Start()
    {
        if (purrSource && !purrSource.isPlaying) purrSource.Play();
        _postureSince = Time.time;
        _lastHappinessMark = happiness;
    }

    /// <summary>Called every frame by a PettingHand while its palm touches one of this cat's zones.</summary>
    public void ReportPet(CatPetZone zone, float strokeSpeed, bool scratching, float dt)
    {
        // Bell curve around the ideal speed; a resting hand still counts a little.
        float s = strokeSpeed / Mathf.Max(0.01f, idealStrokeSpeed);
        float speedFactor = Mathf.Clamp01(0.25f + Mathf.Exp(-(s - 1f) * (s - 1f) * 1.2f));
        if (s > 3f) speedFactor *= 0.4f; // too rough
        float scratchBonus = scratching && (zone.zone == CatPetZone.Zone.Chin || zone.zone == CatPetZone.Zone.Head) ? 1.6f : (scratching ? 1.2f : 1f);
        _frameIntensity = Mathf.Max(_frameIntensity, speedFactor * zone.enjoyment * scratchBonus);
        _lastPetTime = Time.time;
        if (_roaming && _roaming.isActiveAndEnabled) _roaming.NotifyPettingContact();
        else HoldPettingPose();
    }

    /// <summary>Called by PettingHand when a hand first touches the cat.</summary>
    public void OnTouchStart(CatPetZone zone)
    {
        _lastPetTime = Time.time;
        if (_roaming && _roaming.isActiveAndEnabled) _roaming.NotifyPettingContact();
        else HoldPettingPose();
    }

    void HoldPettingPose()
    {
        _posture = Posture.Sit;
        _busyUntil = 0f;
        if (!animator || !animator.HasState(0, S_Sit)) return;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        bool goingToSit = animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == S_Sit;
        if (state.shortNameHash != S_Sit && !goingToSit)
            animator.CrossFadeInFixedTime(S_Sit, 0.2f, 0);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float sinceLastPet = Time.time - _lastPetTime;

        // --- Mood ---
        if (_frameIntensity > 0f)
            happiness = Mathf.Clamp01(happiness + _frameIntensity * happinessGain * dt);
        else if (sinceLastPet > 2f)
            happiness = Mathf.Clamp01(happiness - happinessDecay * dt);
        if (_frameIntensity > 0f && IsStationary)
        {
            _continuousPetTime += dt;
            if (_continuousPetTime >= pettingTimeBeforePurr) _purrEarned = true;
        }
        else if (sinceLastPet > 0.25f || !IsStationary) _continuousPetTime = 0f;
        if (sinceLastPet >= purrLinger || !IsStationary) _purrEarned = false;
        _frameIntensity = 0f;

        // --- Purr (keeps going a little after you stop, and softly while asleep) ---
        float purrTarget = 0f;
        if (_purrEarned && sinceLastPet < purrLinger)
            purrTarget = Mathf.Clamp01(0.35f + happiness * 0.75f) * (1f - sinceLastPet / Mathf.Max(0.01f, purrLinger) * 0.5f);
        if (Time.time < _foodInterestUntil && IsStationary) purrTarget = Mathf.Max(purrTarget, _foodPurr);
        PurrLevel = Mathf.MoveTowards(PurrLevel, purrTarget, dt * (purrTarget > PurrLevel ? 0.8f : 0.3f));
        if (purrSource)
        {
            purrSource.volume = PurrLevel * purrMaxVolume;
            purrSource.pitch = 0.95f + 0.1f * happiness;
        }

        // --- Gentle meow when happiness climbs past milestones ---
        if (happiness - _lastHappinessMark > 0.3f) { _lastHappinessMark = happiness; TryMeow(); }
        else if (happiness < _lastHappinessMark - 0.3f) _lastHappinessMark = happiness;

        if (IsBeingPetted)
        {
            if (!MotionControlled) HoldPettingPose();
            return;
        }
        if (MotionControlled) return;
        if (Time.time < _busyUntil) return;

        // --- Posture ---
        float inPosture = Time.time - _postureSince;
        switch (_posture)
        {
            case Posture.Sit:
                if (happiness > lieDownHappiness && sinceLastPet < 1f && inPosture > minSitTimeBeforeLie)
                {
                    Play(S_SitToLie, 0.25f, 1.4f); // controller auto-transitions to Lie
                    _posture = Posture.Lie; _postureSince = Time.time;
                }
                else if (sinceLastPet > 20f && Time.time - _lastMeow > meowCooldown * 2f)
                {
                    TryMeow(); // asks for attention
                }
                break;
            case Posture.Lie:
                if (sinceLastPet > sleepAfterIdle && happiness > 0.5f)
                {
                    Play(S_Sleep, 1.2f, 0f);
                    _posture = Posture.Sleep; _postureSince = Time.time;
                }
                else if (sinceLastPet > sitUpAfterIdle && happiness < 0.35f)
                {
                    Play(S_LieToSit, 0.25f, 1.4f);
                    _posture = Posture.Sit; _postureSince = Time.time;
                }
                break;
            case Posture.Sleep:
                if (happiness < 0.25f)
                {
                    Play(S_Lie, 0.8f, 0f);
                    _posture = Posture.Lie; _postureSince = Time.time;
                }
                break;
        }
    }

    void TryMeow()
    {
        if (Time.time - _lastMeow < meowCooldown || _posture == Posture.Sleep) return;
        _lastMeow = Time.time;
        if (voiceSource && gentleMeows != null && gentleMeows.Length > 0)
            voiceSource.PlayOneShot(gentleMeows[Random.Range(0, gentleMeows.Length)]);
        if (Time.time >= _busyUntil)
            Play(_posture == Posture.Sit ? S_MeowSit : S_MeowLie, 0.2f, 1.6f);
    }

    void Play(int state, float fade, float busy)
    {
        if (MotionControlled || IsBeingPetted) return;
        if (animator && animator.HasState(0, state))
            animator.CrossFadeInFixedTime(state, fade, 0);
        _busyUntil = Time.time + busy;
    }

    /// <summary>
    /// Procedurally builds a seamless 2 s purr loop: a ~26 Hz pulse train of filtered noise,
    /// with a louder exhale and softer inhale phase.
    /// </summary>
    public static AudioClip CreatePurrClip()
    {
        const int rate = 44100;
        const float length = 2f;
        const float pulseHz = 26f; // 52 pulses in 2 s -> loops seamlessly
        int n = (int)(rate * length);
        var data = new float[n];
        var rng = new System.Random(1234);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.08f;     // low-pass -> rumbly
            lp2 += (lp - lp2) * 0.15f;
            float pulse = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * pulseHz * t), 3f);
            float breath = t < 1.1f ? Mathf.Sin(Mathf.PI * t / 1.1f) : 0.55f * Mathf.Sin(Mathf.PI * (t - 1.1f) / 0.9f);
            float body = Mathf.Sin(2f * Mathf.PI * pulseHz * 2f * t) * 0.25f; // tonal component
            data[i] = (lp2 * 6f + body) * pulse * (0.25f + 0.75f * breath);
        }
        float max = 0f;
        for (int i = 0; i < n; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
        if (max > 0f) for (int i = 0; i < n; i++) data[i] = data[i] / max * 0.9f;
        var clip = AudioClip.Create("ProceduralPurr", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

