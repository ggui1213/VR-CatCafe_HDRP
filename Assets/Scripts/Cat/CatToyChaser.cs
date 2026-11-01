using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CatIndoorRoaming), typeof(NavMeshAgent))]
public class CatToyChaser : MonoBehaviour
{
    public Transform leftPaw, rightPaw;
    public bool IsPlaying => wand != null;
    public string Activity => !IsPlaying ? "Idle" : attacking ? "Paw / pounce" : "Chasing toy";
    public bool IsWatching => IsPlaying && !attacking && motion == Motion.Watching && Time.time >= readyAt;
    public Vector3 LookTarget => wand ? wand.ToyAimPosition : transform.position + transform.forward;
    enum Motion { Watching, Turning, Walking }
    Motion motion;
    CatTeaserWand wand;
    CatIndoorRoaming roaming;
    CatTreatFeeding feeding;
    CatPerching perching;
    PettableCat petting;
    NavMeshAgent agent;
    Animator animator;
    NavMeshPath path;
    float readyAt, nextPath, attackStarted, attackDuration, nextAttack, catchUntil;
    bool standNext, attacking, jumping, hitThisAttack;
    bool useBothPaws;
    float groundHeight;
    float jumpLift;
    Transform catchPaw;
    Vector3 smoothAim, aimVelocity, lastPathGoal;
    bool nearToy, turningInPlace;
    float facingSince = -1f;

    void Awake()
    {
        roaming = GetComponent<CatIndoorRoaming>();
        feeding = GetComponent<CatTreatFeeding>();
        perching = GetComponent<CatPerching>();
        petting = GetComponent<PettableCat>();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        path = new NavMeshPath();
    }

    public bool BeginPlay(CatTeaserWand target)
    {
        if (!isActiveAndEnabled || IsPlaying || (feeding && feeding.IsBusy) || !target.IsHeld || petting.IsBeingPetted ||
            !agent.enabled || !agent.isOnNavMesh || (perching && perching.IsBusy)) return false;
        if (perching) perching.CancelPlan();
        wand = target;
        agent.ResetPath(); agent.isStopped = true;
        roaming.ResetTurnAnimation();
        smoothAim = target.ToyAimPosition;
        aimVelocity = Vector3.zero;
        lastPathGoal = Vector3.positiveInfinity;
        nearToy = turningInPlace = false;
        motion = Motion.Watching;
        facingSince = -1f;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        standNext = state.IsName("Lie") || state.IsName("Sleep") || state.IsName("SitToLie");
        Play(standNext ? "LieToSit" : "StandUp");
        readyAt = Time.time + (standNext ? 1.4f : 1.1f);
        nextAttack = readyAt;
        return true;
    }

    void Update()
    {
        if (!wand) return;
        if (!wand.IsHeld || petting.IsBeingPetted || !agent.enabled || !agent.isOnNavMesh)
        { EndPlay(); return; }
        // Filter physical feather oscillations instead of reversing the feet every frame.
        smoothAim = Vector3.SmoothDamp(smoothAim, wand.ToyAimPosition, ref aimVelocity, 0.18f);
        if (Time.time < readyAt) return;
        if (standNext) { standNext = false; Play("StandUp"); readyAt = Time.time + 1.1f; return; }
        if (attacking) { UpdateAttack(); return; }
        Vector3 direction = smoothAim - transform.position;
        float height = direction.y;
        direction.y = 0f;
        float distance = direction.magnitude;
        float error = direction.sqrMagnitude > 0.001f ? Vector3.SignedAngle(transform.forward, direction, Vector3.up) : 0;
        nearToy = nearToy ? distance < 0.68f : distance < 0.46f;
        if (distance < 0.12f) error = 0f;
        if (nearToy)
        {
            agent.isStopped = true;
            FaceOrWatch(error);
            bool settled = !turningInPlace && Mathf.Abs(error) < 16f && Mathf.Abs(animator.GetFloat("TurnRate")) < 10f;
            if (!settled) facingSince = -1f;
            else if (facingSince < 0f) facingSince = Time.time;
            if (settled && Time.time - facingSince >= 0.18f && Time.time >= nextAttack &&
                distance < 0.52f && height > -0.12f && height < 1.1f) BeginAttack(height);
            return;
        }
        facingSince = -1f;
        if (Time.time >= nextPath)
        {
            nextPath = Time.time + 0.3f;
            // Keep the cat grounded and on a complete indoor path, even if the toy is over furniture.
            Vector3 goal = smoothAim - direction.normalized * 0.25f;
            goal.y = transform.position.y;
            bool needsPath = !agent.hasPath || (goal - lastPathGoal).sqrMagnitude >= 0.0144f;
            if (needsPath)
            {
                if (NavMesh.SamplePosition(goal, out var hit, 0.8f,
                    new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask }) &&
                    Mathf.Abs(hit.position.y - transform.position.y) < 0.2f && roaming.indoorBounds.Contains(hit.position) &&
                    agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                { agent.SetPath(path); lastPathGoal = goal; }
                else { agent.ResetPath(); agent.isStopped = true; }
            }
        }
        Vector3 heading = distance > 0.65f && agent.hasPath ? agent.steeringTarget - transform.position : direction;
        heading.y = 0;
        error = heading.sqrMagnitude > 0.001f ? Vector3.SignedAngle(transform.forward, heading, Vector3.up) : 0;
        float walkAngle = motion == Motion.Walking ? 80f : 38f;
        bool walk = agent.hasPath && agent.remainingDistance > 0.12f && Mathf.Abs(error) < walkAngle;
        agent.isStopped = !walk;
        float targetSpeed = roaming.walkingSpeed * 1.35f * Mathf.Clamp(Mathf.Cos(error * Mathf.Deg2Rad), 0.3f, 1f);
        agent.speed = Mathf.MoveTowards(agent.speed, targetSpeed, roaming.walkingSpeed * 2f * Time.deltaTime);
        if (walk)
        {
            motion = Motion.Walking;
            turningInPlace = false;
            PlayIfNeeded("Walk");
            roaming.UpdateTurnAnimation(error, roaming.walkTurnLimit);
        }
        else FaceOrWatch(error);
    }

    void FaceOrWatch(float error)
    {
        // Small changes use head gaze. A body turn finishes before reversing or attacking.
        if (!turningInPlace && Mathf.Abs(error) > 16f) turningInPlace = true;
        if (turningInPlace && Mathf.Abs(error) < 5f) turningInPlace = false;
        roaming.UpdateTurnAnimation(turningInPlace ? error : 0f, roaming.stationaryTurnLimit);
        bool finishingStep = Mathf.Abs(animator.GetFloat("TurnRate")) > 8f;
        motion = turningInPlace || finishingStep ? Motion.Turning : Motion.Watching;
        PlayIfNeeded(motion == Motion.Turning ? "Turn" : "ToyWatch");
    }

    void BeginAttack(float height)
    {
        attacking = true; hitThisAttack = false;
        attackStarted = Time.time;
        jumping = height > 0.6f;
        jumpLift = Mathf.Clamp(height - 0.36f, 0.18f, 0.45f);
        attackDuration = jumping ? 1.733333f : 1f;
        catchPaw = leftPaw && rightPaw &&
            (leftPaw.position - wand.ToyAimPosition).sqrMagnitude < (rightPaw.position - wand.ToyAimPosition).sqrMagnitude
                ? leftPaw : rightPaw;
        groundHeight = transform.position.y;
        agent.updatePosition = !jumping;
        roaming.ResetTurnAnimation();
        facingSince = -1f;
        string state = jumping ? "ToyJump" : catchPaw == leftPaw ? "ToyPawLeft" : "ToyPawRight";
        if (!jumping && Random.value < 0.25f) { state = "ToyScratch"; attackDuration = 1.633333f; }
        useBothPaws = jumping || state == "ToyScratch";
        Play(state);
    }

    void UpdateAttack()
    {
        float t = (Time.time - attackStarted) / attackDuration;
        Vector3 aim = wand.ToyAimPosition - transform.position;
        aim.y = 0f;
        // The heading is established by the turn animation; planted paws never swivel during a swipe.
        if (jumping)
        {
            float airborne = Mathf.Clamp01((t - 0.16f) / 0.63f);
            var position = transform.position;
            position.y = groundHeight + Mathf.Sin(airborne * Mathf.PI) * jumpLift;
            transform.position = position;
        }
        if (t > 0.18f && t < 0.82f && !hitThisAttack && Vector3.Angle(transform.forward, aim) < 40f)
        {
            foreach (var paw in useBothPaws ? new[] { leftPaw, rightPaw } : new[] { catchPaw })
                if (paw && wand.Bat(paw.position, paw.position - transform.position, Random.value < 0.35f))
                { hitThisAttack = true; catchPaw = paw; break; }
        }
        if (Time.time < catchUntil && catchPaw)
        {
            Vector3 pull = (catchPaw.position - wand.lure.position) * 22f - wand.lure.linearVelocity * 2f;
            wand.lure.AddForce(Vector3.ClampMagnitude(pull, 4f), ForceMode.Acceleration);
        }
        if (t >= 1f)
        {
            RestoreGround();
            attacking = false;
            nextAttack = Time.time + Random.Range(0.45f, 1.1f);
            motion = Motion.Watching;
            turningInPlace = false;
            Play(jumping ? "Land" : "ToyWatch");
            readyAt = Time.time + (jumping ? 0.45f : 0.1f);
        }
    }

    public void StartCatch() { catchUntil = Time.time + 0.18f; }

    public void ApplyRootTurn(Quaternion delta)
    {
        if (attacking || Time.time < readyAt || motion == Motion.Watching) return;
        Vector3 forward = (transform.rotation * delta) * Vector3.forward;
        forward.y = 0;
        if (forward.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(forward);
    }

    void RestoreGround()
    {
        if (!jumping || !agent) return;
        var position = transform.position; position.y = groundHeight;
        transform.position = position;
        if (agent.enabled && agent.isOnNavMesh) agent.nextPosition = position;
        agent.updatePosition = true;
    }

    public void EndPlay()
    {
        if (!wand) return;
        RestoreGround();
        wand = null; attacking = false; jumping = false; catchUntil = 0;
        roaming.ResetTurnAnimation();
        if (agent && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true; agent.ResetPath();
            if (roaming && roaming.isActiveAndEnabled) roaming.ResumeAfterPerch();
        }
    }

    void OnDisable() { EndPlay(); }
    void Play(string state) { animator.CrossFadeInFixedTime(state, 0.2f, 0); }
    void PlayIfNeeded(string state)
    {
        if (!animator.GetCurrentAnimatorStateInfo(0).IsName(state) &&
            !(animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(state))) Play(state);
    }
}
