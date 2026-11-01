using UnityEngine;

// Runs after palm detection and animation so blinking clips cannot overwrite petting eyes.
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(PettableCat))]
public class CatPettingExpression : MonoBehaviour
{
    public Transform[] eyelids;
    public Quaternion[] openRotations;
    public Quaternion[] closedRotations;
    [Range(0f, 1f)] public float squintAmount = 0.88f;
    public float fadeSpeed = 5f;
    public float Squint { get; private set; }
    PettableCat cat;
    CatTreatFeeding feeding;
    void Awake() { cat = GetComponent<PettableCat>(); feeding = GetComponent<CatTreatFeeding>(); }
    void LateUpdate()
    {
        float target = cat.IsBeingPetted ? squintAmount : feeding && feeding.IsContent ? 0.75f : feeding && feeding.IsEating ? 0.5f : 0f;
        Squint = Mathf.MoveTowards(Squint, target, Time.deltaTime * fadeSpeed);
        if (eyelids == null || openRotations == null || closedRotations == null) return;
        for (int i = 0; i < eyelids.Length && i < openRotations.Length && i < closedRotations.Length; i++)
            if (eyelids[i] && Squint > 0f)
                eyelids[i].localRotation = Quaternion.Slerp(openRotations[i], closedRotations[i], Squint);
    }
    void OnDisable()
    {
        Squint = 0f;
        if (eyelids == null || openRotations == null) return;
        for (int i = 0; i < eyelids.Length && i < openRotations.Length; i++)
            if (eyelids[i]) eyelids[i].localRotation = openRotations[i];
    }
}
