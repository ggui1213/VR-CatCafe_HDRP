using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CatIndoorRoaming))]
public class CatPerching : MonoBehaviour
{
    public Vector2 perchDuration = new Vector2(12f, 28f);
    [Range(0f, 1f)] public float visitChance = 0.35f;
    public bool IsBusy => phase != Stage.Ground;
    public bool IsResting => phase == Stage.Rest || phase == Stage.Lying;
    public bool HasPlan => spot != null && !IsBusy;
    public Vector3 TakeoffDirection => spot ? spot.transform.position - transform.position : transform.forward;
    public string Activity => phase.ToString();
    enum Stage { Ground, JumpUp, LandUp, FaceRest, Rest, LieDown, Lying, SitUp, Stand, JumpDown, LandDown }
    Stage phase;
    CatPerchSpot spot;
    CatIndoorRoaming roaming;
    NavMeshAgent agent;
    Animator animator;
    PettableCat cat;
    Vector3 approach, departure, from, to;
    float until, started, duration, nextVisit, nextVariation;
    public bool TryPlan(NavMeshPath path, CatPerchSpot requested = null)
    {
        if (IsBusy) return false;
        CancelPlan();
        if (!requested && (Time.time < nextVisit || Random.value > visitChance)) return false;
        var spots = requested ? new[] { requested } : FindObjectsByType<CatPerchSpot>(FindObjectsSortMode.None);
        int offset = spots.Length == 0 ? 0 : Random.Range(0, spots.Length);
        for (int n = 0; n < spots.Length; n++)
        {
            var candidate = spots[(n + offset) % spots.Length];
            if (!candidate.IsClear(this)) continue;
            Vector3 landing = candidate.transform.position;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                Vector3 target = landing + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (candidate.radius + 0.55f);
                target.y = transform.position.y;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(target, out hit, 0.25f, new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask })) continue;
                if (Mathf.Abs(hit.position.y - transform.position.y) > 0.15f || !roaming.indoorBounds.Contains(hit.position)) continue;
                if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                if (!ClearArc(hit.position, landing, candidate.support)) continue;
                if (!candidate.Reserve(this)) break;
                spot = candidate; approach = hit.position;
                return agent.SetPath(path);
            }
        }
        nextVisit = Time.time + 5f;
        return false;
    }
    void Awake()
    {
        roaming = GetComponent<CatIndoorRoaming>();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        cat = GetComponent<PettableCat>();
    }
    public void CancelPlan() { if (spot) spot.Release(this); spot = null; }
    public bool BeginJump()
    {
        if (!HasPlan || !spot.IsClear(this)) { CancelPlan(); return false; }
        departure = approach;
        agent.isStopped = true; agent.ResetPath(); agent.enabled = false;
        BeginArc(Stage.JumpUp, spot.transform.position);
        return true;
    }
    void BeginArc(Stage stage, Vector3 destination)
    {
        phase = stage; from = transform.position; to = destination;
        started = Time.time; duration = 1.733333f;
        Play("Jump", 0.08f);
    }
    public void NotifyPettingContact()
    {
        if (phase == Stage.FaceRest) { phase = Stage.Rest; Play("SitDown"); started = Time.time; }
        if (!IsResting && phase != Stage.LieDown && phase != Stage.SitUp) return;
        until = Mathf.Max(until, Time.time + 4f);
        nextVariation = Time.time + 6f;
        if (phase == Stage.Lying || phase == Stage.LieDown)
        { phase = Stage.SitUp; Play("LieToSit"); started = Time.time; }
        else if (phase == Stage.Rest) PlayIfNeeded("Sit");
    }
    void Update()
    {
        if (!IsBusy) return;
        if (cat.IsBeingPetted) NotifyPettingContact();
        float now = Time.time;
        if (phase == Stage.JumpUp || phase == Stage.JumpDown)
        {
            float elapsed = (now - started) / duration;
            // Match the clip's anticipation, airborne portion and landing; do not move during crouching.
            float t = Mathf.Clamp01((elapsed - 0.16f) / 0.63f);
            transform.position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.28f;
            if (elapsed >= 0.79f)
            { transform.position = to; phase = phase == Stage.JumpUp ? Stage.LandUp : Stage.LandDown; Play("Land", 0.08f); started = now; }
        }
        else if (phase == Stage.LandUp && now - started >= 0.45f)
        { phase = Stage.FaceRest; Play("Turn"); }
        else if (phase == Stage.FaceRest)
        {
            Vector3 direction = roaming.player ? roaming.player.position - transform.position : transform.forward;
            direction.y = 0f;
            float error = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
            roaming.UpdateTurnAnimation(error, roaming.stationaryTurnLimit);
            if (Mathf.Abs(error) < 8f)
            { animator.SetFloat("TurnRate", 0f); phase = Stage.Rest; Play("SitDown"); started = now; until = now + Random.Range(perchDuration.x, perchDuration.y); nextVariation = now + 3f; }
        }
        else if (phase == Stage.Rest)
        {
            if (now - started >= 1f && animator.GetCurrentAnimatorStateInfo(0).IsName("SitDown")) Play("Sit");
            if (!cat.IsBeingPetted && now >= until) { phase = Stage.Stand; Play("StandUp"); started = now; }
            else if (!cat.IsBeingPetted && now >= nextVariation)
            {
                if (spot && spot.allowLying && spot.IsClear(this) && Random.value < 0.65f)
                { phase = Stage.LieDown; Play("SitToLie"); started = now; }
                else Play(Random.value < 0.5f ? "LickPaw" : "LickChest");
                nextVariation = now + Random.Range(6f, 10f);
            }
        }
        else if (phase == Stage.LieDown && now - started >= 1.333334f)
        { phase = Stage.Lying; Play("Lie"); }
        else if (phase == Stage.Lying && now >= until && !cat.IsBeingPetted)
        { phase = Stage.SitUp; Play("LieToSit"); started = now; }
        else if (phase == Stage.SitUp && now - started >= 1.333334f)
        { phase = Stage.Rest; Play("Sit"); started = now - 2f; }
        else if (phase == Stage.Stand && now - started >= 1f)
        {
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(departure, out hit, 0.3f, new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask }))
            { phase = Stage.Rest; Play("Sit"); until = now + 3f; return; }
            // Turn through the existing root-motion blendspace before jumping back down.
            Vector3 direction = hit.position - transform.position; direction.y = 0;
            float error = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
            if (Mathf.Abs(error) > 6f)
            { PlayIfNeeded("Turn"); roaming.UpdateTurnAnimation(error, roaming.stationaryTurnLimit); return; }
            animator.SetFloat("TurnRate", 0f);
            if (!ClearArc(transform.position, hit.position, spot ? spot.support : null))
            { phase = Stage.Rest; Play("Sit"); until = now + 3f; return; }
            BeginArc(Stage.JumpDown, hit.position);
        }
        else if (phase == Stage.LandDown && now - started >= 0.45f)
        {
            agent.enabled = true; agent.Warp(transform.position);
            phase = Stage.Ground; CancelPlan(); nextVisit = now + Random.Range(10f, 20f);
            roaming.ResumeAfterPerch();
        }
    }
    public void ApplyRootTurn(Quaternion delta)
    {
        if (phase != Stage.Stand && phase != Stage.FaceRest) return;
        Vector3 f = (transform.rotation * delta) * Vector3.forward; f.y = 0;
        if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f);
    }
    bool ClearArc(Vector3 start, Vector3 end, Collider support)
    {
        Vector3 previous = start + Vector3.up * 0.2f;
        for (int i = 1; i <= 12; i++)
        {
            float t = i / 12f;
            Vector3 current = Vector3.Lerp(start, end, t) + Vector3.up * (0.2f + Mathf.Sin(t * Mathf.PI) * 0.28f);
            foreach (var hit in Physics.SphereCastAll(previous, 0.10f, current - previous, Vector3.Distance(previous, current), ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != support && hit.collider.GetComponentInParent<PettableCat>() != cat) return false;
            previous = current;
        }
        return true;
    }
    void Play(string state, float fade = 0.15f) { animator.CrossFadeInFixedTime(state, fade, 0); }
    void PlayIfNeeded(string state)
    {
        if (!animator.GetCurrentAnimatorStateInfo(0).IsName(state) &&
            !(animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(state))) Play(state);
    }
    void OnDisable()
    {
        if (agent && !agent.enabled) { transform.position = departure; agent.enabled = true; agent.Warp(departure); }
        phase = Stage.Ground;
        CancelPlan();
    }
}
