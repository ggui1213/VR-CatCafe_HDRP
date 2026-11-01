using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// Lives on a small trigger sphere at the palm of a controller-driven hand model.
/// While touching a CatPetZone it reports strokes to the cat and plays a continuous haptic
/// "texture" on the controller: soft contact + fur grain that scales with stroke speed,
/// a buzzier pattern while scratching (grip), and the cat's purr rumble.
/// </summary>
[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public class PettingHand : MonoBehaviour
{
    [Header("References")]
    public ControllerHandPoser poser;
    public HapticImpulsePlayer haptics;
    public UnityEngine.XR.XRNode controllerNode = UnityEngine.XR.XRNode.LeftHand;
    public float LastHapticAmplitude { get; private set; }
    public bool LastHapticSucceeded { get; private set; }
    public int HapticPulseCount { get; private set; }
    readonly Dictionary<CatPetZone, float> _lastSeen = new Dictionary<CatPetZone, float>();
    CatPetZone _strokeZone;
    Vector3 _lastRelativePalm;

    [Header("Haptic texture")]
    [Range(0f, 1f)] public float contactAmplitude = 0.05f;
    [Range(0f, 1f)] public float furAmplitude = 0.35f;
    [Tooltip("Stroke speed (m/s) at which fur haptics reach full strength.")]
    public float furFullSpeed = 0.4f;
    [Tooltip("Fur 'bumps' per meter of travel; higher = finer grain.")]
    public float furGrainPerMeter = 60f;
    [Range(0f, 1f)] public float scratchAmplitude = 0.15f;
    [Range(0f, 1f)] public float purrAmplitude = 0.2f;
    [Range(0f, 1f)] public float touchTapAmplitude = 0.25f;
    public float hapticInterval = 0.033f;
    [Range(0f, 1f)] public float maxAmplitude = 0.7f;

    readonly HashSet<CatPetZone> _zones = new HashSet<CatPetZone>();
    readonly List<CatPetZone> _toRemove = new List<CatPetZone>();
    Vector3 _lastPos;
    readonly HashSet<CatPetZone> _overlappingZones = new HashSet<CatPetZone>();
    readonly HashSet<PettableCat> _previousCats = new HashSet<PettableCat>();
    Collider[] _overlapBuffer = new Collider[32];
    SphereCollider _palmCollider;
    static int _lastPhysicsSyncFrame = -1;
    float _speed, _furPhase, _nextHaptic;
    float _seed;

    public bool IsTouching => _zones.Count > 0;
    public float StrokeSpeed => _speed;
    public CatPetZone ContactZone { get; private set; }

    void Awake()
    {
        var col = GetComponent<SphereCollider>();
        _palmCollider = col;
        col.isTrigger = true;
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        if (!poser) poser = GetComponentInParent<ControllerHandPoser>();
        if (!haptics) haptics = GetComponentInParent<HapticImpulsePlayer>();
        _seed = Random.value * 100f;
    }

    void OnEnable()
    {
        _lastPos = transform.position;
        EnsureHapticActionEnabled();
    }
    void EnsureHapticActionEnabled()
    {
        if (!haptics || haptics.hapticOutput == null) return;
        var reference = haptics.hapticOutput.inputActionReference;
        if (reference && reference.action != null && !reference.action.enabled) reference.action.Enable();
    }

    void OnDisable()
    {
        _zones.Clear();
        ContactZone = null;
        _lastSeen.Clear();
        LastHapticAmplitude = 0f;
        if (poser) poser.isTouchingPettable = false;
    }

    void RefreshContacts()
    {
        if (!_palmCollider.enabled) { _zones.Clear(); return; }
        if (_lastPhysicsSyncFrame != Time.frameCount)
        {
            Physics.SyncTransforms();
            _lastPhysicsSyncFrame = Time.frameCount;
        }
        Vector3 scale = transform.lossyScale;
        float radius = _palmCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 center = transform.TransformPoint(_palmCollider.center);
        int count = Physics.OverlapSphereNonAlloc(center, radius, _overlapBuffer, ~0, QueryTriggerInteraction.Collide);
        if (count == _overlapBuffer.Length)
        {
            _overlapBuffer = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide);
            count = _overlapBuffer.Length;
        }
        _previousCats.Clear();
        foreach (var zone in _zones) if (zone && zone.Cat) _previousCats.Add(zone.Cat);
        _overlappingZones.Clear();
        for (int i = 0; i < count; i++)
        {
            var zone = _overlapBuffer[i].GetComponent<CatPetZone>();
            if (zone && zone.isActiveAndEnabled && zone.Cat && zone.Cat.isActiveAndEnabled)
            {
                _overlappingZones.Add(zone);
                _lastSeen[zone] = Time.time;
            }
        }
        // Bridge brief gaps between adjacent animated fur zones without repeated taps.
        foreach (var zone in _zones)
        {
            float seen;
            if (zone && zone.isActiveAndEnabled && zone.Cat && zone.Cat.isActiveAndEnabled &&
                zone.GetComponent<Collider>().enabled && _lastSeen.TryGetValue(zone, out seen) && Time.time - seen < 0.12f)
                _overlappingZones.Add(zone);
        }
        _zones.Clear();
        foreach (var zone in _overlappingZones)
        {
            _zones.Add(zone);
            if (_previousCats.Add(zone.Cat))
            {
                zone.Cat.OnTouchStart(zone);
                SendFeedback(touchTapAmplitude, 0.035f);
                _nextHaptic = Time.time + 0.05f;
            }
        }
    }

    void LateUpdate()
    {
        RefreshContacts();
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // Drop zones that were disabled/destroyed while touching
        _toRemove.Clear();
        foreach (var z in _zones) if (!z || !z.isActiveAndEnabled) _toRemove.Add(z);
        foreach (var z in _toRemove) _zones.Remove(z);

        bool touching = _zones.Count > 0;
        if (poser) poser.isTouchingPettable = touching;
        if (!touching) { ContactZone = null; _strokeZone = null; _speed = 0f; LastHapticAmplitude = 0f; return; }

        // Pick the zone the cat enjoys most (e.g. chin beats back when both overlap)
        CatPetZone best = null;
        foreach (var z in _zones) if (!best || z.enjoyment > best.enjoyment) best = z;
        ContactZone = best;

        // Measure the palm relative to the touched body part, not cat locomotion.
        Vector3 relative = best.transform.InverseTransformPoint(transform.position);
        float strokeSpeed = _strokeZone == best
            ? best.transform.TransformVector(relative - _lastRelativePalm).magnitude / dt : 0f;
        _strokeZone = best;
        _lastRelativePalm = relative;
        _speed = Mathf.Lerp(_speed, Mathf.Min(strokeSpeed, 1.5f), 1f - Mathf.Exp(-12f * dt));
        float grip = poser ? poser.GripValue : 0f;
        bool scratching = grip > 0.2f;
        best.Cat.ReportPet(best, _speed, scratching, dt);

        // ---- Haptic texture ----
        _furPhase += _speed * dt * furGrainPerMeter * Mathf.Max(0.1f, best.furDensity);
        if (Time.time < _nextHaptic) return;
        _nextHaptic = Time.time + hapticInterval;

        float grain = Mathf.PerlinNoise(_furPhase, _seed);              // uneven fur bumps
        float speed01 = Mathf.Clamp01(_speed / furFullSpeed);
        float fur = furAmplitude * speed01 * (0.55f + 0.45f * grain) * best.furDensity;

        float scratch = scratching
            ? scratchAmplitude * grip * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * 9f))
            : 0f;

        float purrLevel = best.Cat.PurrLevel;
        float purr = purrAmplitude * purrLevel * (0.6f + 0.4f * Mathf.Sin(Time.time * Mathf.PI * 2f * 2.6f));

        float amp = Mathf.Min(maxAmplitude, contactAmplitude + fur + scratch + purr);
        SendFeedback(amp, hapticInterval * 0.9f);
    }

    void SendFeedback(float amplitude, float duration)
    {
        EnsureHapticActionEnabled();
        LastHapticAmplitude = Mathf.Clamp(amplitude, 0f, maxAmplitude);
        HapticPulseCount++;
        LastHapticSucceeded = haptics && haptics.SendHapticImpulse(LastHapticAmplitude, duration);
        if (!LastHapticSucceeded)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(controllerNode);
            UnityEngine.XR.HapticCapabilities caps;
            if (device.isValid && device.TryGetHapticCapabilities(out caps) && caps.supportsImpulse)
                LastHapticSucceeded = device.SendHapticImpulse(0, LastHapticAmplitude, duration);
        }
    }
}


