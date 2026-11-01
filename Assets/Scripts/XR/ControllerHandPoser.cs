using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives finger bones of a non hand-tracked hand mesh from controller input.
/// Trigger -> index, Grip -> middle/ring/little, thumb curls on touch or when grip/trigger pressed.
/// Built for XR Hands sample models (fingers along local +Z, palm facing -Y, joints curl around local +X).
/// </summary>
[DefaultExecutionOrder(100)]
public class ControllerHandPoser : MonoBehaviour
{
    [Header("Input (0..1)")]
    public InputActionProperty triggerAction;
    public InputActionProperty gripAction;
    [Tooltip("Optional touch/button that curls the thumb.")]
    public InputActionProperty thumbTouchAction;
    [Tooltip("When true, the thumb will default to resting on the controller face/joystick rather than wide open.")]
    public bool defaultThumbRest = true;
    [Range(0f, 1f)] public float thumbRestAmount = 0.6f;

    [Header("Finger bones (auto-found if empty)")]
    public Transform[] thumb = new Transform[0];
    public Transform[] index = new Transform[0];
    public Transform[] middle = new Transform[0];
    public Transform[] ring = new Transform[0];
    public Transform[] little = new Transform[0];

    [Header("Curl settings")]
    public Vector3 fingerCurlAxis = Vector3.right;
    [Tooltip("Max curl for proximal, intermediate, distal.")]
    public Vector3 fingerCurlAngles = new Vector3(70f, 90f, 60f);
    public Vector3 thumbCurlAxis = Vector3.right;
    [Tooltip("Max curl for thumb metacarpal, proximal, distal.")]
    public Vector3 thumbCurlAngles = new Vector3(10f, 30f, 35f);
    [Range(0f, 1f)] public float thumbPressThreshold = 0.5f;
    public float smoothSpeed = 20f;

    [Header("Natural grip / tickle")]
    [Tooltip("Finger curl when no button is pressed (fingers lightly wrapped around the controller).")]
    [Range(0f, 1f)] public float fingerRestAmount = 0.25f;
    [Tooltip("Grip curls ALL four fingers into a scratching claw. This is the max curl reached at full grip.")]
    [Range(0f, 1f)] public float gripClawAmount = 0.7f;
    [Tooltip("Finger wiggle amplitude while scratching (grip held while touching a cat).")]
    [Range(0f, 0.5f)] public float scratchWiggleAmount = 0.18f;
    public float scratchWiggleSpeed = 16f;
    [Header("Automatic petting on contact")]
    public bool autoPetOnContact = true;
    [Range(0f, 1f)] public float pettingFingerCurl = 0.28f;
    [Range(0f, 0.3f)] public float pettingFingerMotion = 0.075f;
    public float pettingMotionSpeed = 2.8f;
    public float AutomaticPettingWeight { get; private set; }
    public bool IsHoldingWand { get; set; }
    public bool IsHoldingTreat { get; set; }

    /// <summary>Set by PettingHand: true while the palm is touching something pettable.</summary>
    [System.NonSerialized] public bool isTouchingPettable;

    /// <summary>Smoothed grip value (0..1), read by PettingHand to detect scratching.</summary>
    public float GripValue => _grip;
    public float TriggerValue => _index;

    [Header("Preview")]
    [Range(0f, 1f)] public float previewTrigger;
    [Range(0f, 1f)] public float previewGrip;
    [Range(0f, 1f)] public float previewThumb;

    readonly Dictionary<Transform, Quaternion> _open = new Dictionary<Transform, Quaternion>();
    float _index, _grip, _thumb, _wiggle;

    void Reset() { AutoFindBones(); }

    void Awake()
    {
        if (index.Length == 0 || middle.Length == 0) AutoFindBones();
        CacheOpenPose();
    }

    void OnEnable()
    {
        EnableIfNeeded(triggerAction);
        EnableIfNeeded(gripAction);
        EnableIfNeeded(thumbTouchAction);
    }

    static void EnableIfNeeded(InputActionProperty p)
    {
        var a = p.action;
        if (a != null && !a.enabled) a.Enable();
    }

    static float Read(InputActionProperty p)
    {
        var a = p.action;
        if (a == null || !a.enabled) return 0f;
        if (a.type == InputActionType.Button) return a.IsPressed() ? 1f : 0f;
        return a.ReadValue<float>();
    }

    void Update()
    {
        float ti = Mathf.Max(Read(triggerAction), previewTrigger);
        float tg = Mathf.Max(Read(gripAction), previewGrip);
        
        float baseThumb = defaultThumbRest ? thumbRestAmount : 0f;
        float touchInput = Read(thumbTouchAction);
        if (touchInput > 0f)
            baseThumb = Mathf.Max(baseThumb, touchInput);

        float tt = Mathf.Max(Mathf.Max(baseThumb, previewThumb),
            (tg > thumbPressThreshold || ti > thumbPressThreshold) ? 1f : 0f);

        float k = smoothSpeed <= 0f ? 1f : 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        _index = Mathf.Lerp(_index, ti, k);
        _grip = Mathf.Lerp(_grip, tg, k);
        _thumb = Mathf.Lerp(_thumb, tt, k);

    }

    void LateUpdate()
    {
        if (Application.isPlaying && IsHoldingTreat)
        {
            AutomaticPettingWeight = 0f;
            ApplyPose(Mathf.Lerp(0.45f, 0.85f, _index), 0.8f, 0.85f, 0f);
            return;
        }
        if (Application.isPlaying && IsHoldingWand)
        {
            AutomaticPettingWeight = 0f;
            ApplyPose(0.82f, 0.88f, 0.9f, 0f);
            return;
        }
        // Palm detection runs first. Contact alone starts the gentle stroke pose.
        float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
        float target = autoPetOnContact && isTouchingPettable ? 1f : 0f;
        AutomaticPettingWeight = Mathf.Lerp(AutomaticPettingWeight, target, k);
        float wiggleTarget = isTouchingPettable && _grip > 0.2f ? 1f : 0f;
        _wiggle = Mathf.Lerp(_wiggle, wiggleTarget, k);
        float claw = fingerRestAmount + (gripClawAmount - fingerRestAmount) * _grip;
        float indexCurl = Mathf.Max(claw, Mathf.Lerp(fingerRestAmount, 1f, _index));
        float gentle = AutomaticPettingWeight * (1f - _grip);
        float wave = Mathf.Sin(Time.time * Mathf.PI * 2f * pettingMotionSpeed) * pettingFingerMotion * gentle;
        claw = Mathf.Lerp(claw, pettingFingerCurl, gentle) + wave;
        indexCurl = Mathf.Lerp(indexCurl, pettingFingerCurl, gentle * (1f - _index)) + wave;
        if (Application.isPlaying)
            ApplyPose(Mathf.Clamp01(indexCurl), Mathf.Clamp01(claw), _thumb, _wiggle);
    }

    public void ApplyPose(float indexCurl, float gripCurl, float thumbCurl) => ApplyPose(indexCurl, gripCurl, thumbCurl, 0f);

    public void ApplyPose(float indexCurl, float gripCurl, float thumbCurl, float wiggle)
    {
        if (_open.Count == 0) CacheOpenPose();
        float t = Time.time * scratchWiggleSpeed;
        float w = wiggle * scratchWiggleAmount;
        // Each finger out of phase so it reads as scratching, not a pulsing fist.
        Curl(index, Mathf.Clamp01(indexCurl + w * Mathf.Sin(t)), fingerCurlAxis, fingerCurlAngles);
        Curl(middle, Mathf.Clamp01(gripCurl + w * Mathf.Sin(t + 1.6f)), fingerCurlAxis, fingerCurlAngles);
        Curl(ring, Mathf.Clamp01(gripCurl + w * Mathf.Sin(t + 3.2f)), fingerCurlAxis, fingerCurlAngles);
        Curl(little, Mathf.Clamp01(gripCurl + w * Mathf.Sin(t + 4.8f)), fingerCurlAxis, fingerCurlAngles);
        Curl(thumb, thumbCurl, thumbCurlAxis, thumbCurlAngles);
    }

    void Curl(Transform[] bones, float amount, Vector3 axis, Vector3 angles)
    {
        if (bones == null) return;
        Vector3 ax = axis.sqrMagnitude > 0f ? axis.normalized : Vector3.right;
        for (int i = 0; i < bones.Length; i++)
        {
            Transform b = bones[i];
            Quaternion o;
            if (b == null || !_open.TryGetValue(b, out o)) continue;
            float max = i < 3 ? angles[i] : angles.z;
            b.localRotation = o * Quaternion.AngleAxis(max * amount, ax);
        }
    }

    void CacheOpenPose()
    {
        _open.Clear();
        foreach (var arr in new[] { thumb, index, middle, ring, little })
        {
            if (arr == null) continue;
            foreach (var b in arr)
                if (b != null && !_open.ContainsKey(b)) _open[b] = b.localRotation;
        }
    }

    [ContextMenu("Auto Find Bones")]
    public void AutoFindBones()
    {
        var all = GetComponentsInChildren<Transform>(true);
        thumb = Chain(all, "Thumb", new[] { "Metacarpal", "Proximal", "Distal" });
        index = Chain(all, "Index", new[] { "Proximal", "Intermediate", "Distal" });
        middle = Chain(all, "Middle", new[] { "Proximal", "Intermediate", "Distal" });
        ring = Chain(all, "Ring", new[] { "Proximal", "Intermediate", "Distal" });
        little = Chain(all, "Little", new[] { "Proximal", "Intermediate", "Distal" });
    }

    static Transform[] Chain(Transform[] all, string finger, string[] joints)
    {
        var list = new List<Transform>();
        foreach (var j in joints)
            foreach (var t in all)
                if (t.name.Contains(finger) && t.name.Contains(j)) { list.Add(t); break; }
        return list.ToArray();
    }

    [ContextMenu("Preview Fist")]
    public void PreviewFist() { CacheOpenPose(); ApplyPose(1f, 1f, 1f); }

    [ContextMenu("Preview Open")]
    public void PreviewOpen() { ApplyPose(0f, 0f, 0f); }
}

