using UnityEngine;

/// <summary>
/// A trigger collider on a part of the cat (head, chin, back...).
/// PettingHand detects these and reports strokes to the owning PettableCat.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CatPetZone : MonoBehaviour
{
    public enum Zone { Head, Chin, Back, Belly, Tail }

    public Zone zone = Zone.Back;
    [Tooltip("How much the cat enjoys being petted here (multiplies happiness gain).")]
    public float enjoyment = 1f;
    [Tooltip("Haptic texture multiplier for this area (fur is thicker on the back, softer under the chin).")]
    public float furDensity = 1f;

    public PettableCat Cat { get; private set; }

    void Awake()
    {
        Cat = GetComponentInParent<PettableCat>();
        GetComponent<Collider>().isTrigger = true;
    }
}
