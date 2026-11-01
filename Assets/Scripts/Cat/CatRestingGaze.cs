using UnityEngine;
[DefaultExecutionOrder(900)]
public class CatRestingGaze : MonoBehaviour
{
    public Transform head, player;
    public float yawLimit = 38f, pitchLimit = 20f;
    CatIndoorRoaming roaming;
    CatToyChaser toy;
    CatTreatFeeding feeding;
    PettableCat cat;
    float yaw, pitch, weight;
    void Awake() { roaming = GetComponent<CatIndoorRoaming>(); cat = GetComponent<PettableCat>(); toy = GetComponent<CatToyChaser>(); feeding = GetComponent<CatTreatFeeding>(); }
    void LateUpdate()
    {
        if (!head || !player) return;
        bool watchingToy = toy && toy.IsWatching;
        bool watchingTreat = feeding && feeding.IsWaiting;
        bool resting = (watchingTreat || watchingToy || (roaming && roaming.IsStationary)) && !cat.IsBeingPetted;
        weight = Mathf.MoveTowards(weight, resting ? 1f : 0f, Time.deltaTime * 2f);
        Vector3 d = transform.InverseTransformDirection((watchingTreat ? feeding.LookTarget : watchingToy ? toy.LookTarget : player.position) - head.position);
        float targetYaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -yawLimit, yawLimit);
        float targetPitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x,d.z).magnitude) * Mathf.Rad2Deg, -pitchLimit, pitchLimit);
        yaw = Mathf.Lerp(yaw, targetYaw, 1f - Mathf.Exp(-Time.deltaTime * 3f));
        pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-Time.deltaTime * 3f));
        Quaternion offset = Quaternion.AngleAxis(yaw * weight, transform.up) * Quaternion.AngleAxis(pitch * weight, transform.right);
        head.rotation = offset * head.rotation;
    }
}
