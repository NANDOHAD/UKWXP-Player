using UnityEngine;

public class TestPlayCameraController : MonoBehaviour
{
    [Header("References")]
    public TestPlayController controller;
    public Camera controlledCamera;
    public FreeCam editorCamera;

    [Header("Original-style follow")]
    public bool useLockTargetWhenAvailable = true;
    public float playerPivotHeight = 1.4f;
    public float targetPivotHeight = 1.2f;
    public float followDistance = 7f;
    public float followHeight = 2.4f;
    public float shoulderOffset;
    public float unlockedLookAhead = 2.5f;
    [Range(0f, 1f)]
    public float targetFraming = 0.4f;
    public float targetDistanceScale = 0.12f;
    public float maxTargetDistanceBonus = 5f;
    public bool followLateralMovementHeading = true;
    [Range(0f, 1f)]
    public float lateralMovementHeadingWeight = 0.5f;
    [Range(0f, 90f)]
    public float maxLockedOrbitAngle = 60f;
    public bool followBackwardMovement = true;
    [Min(0f)]
    public float backwardDistanceBonus = 2f;
    [Min(0f)]
    public float backwardHeightBonus = 0.4f;

    [Header("Response")]
    [Min(0.001f)]
    public float positionSmoothTime = 0.1f;
    [Min(0f)]
    public float rotationSharpness = 14f;
    [Min(0f)]
    public float teleportSnapDistance = 12f;
    [Tooltip("原作投影行列で確認できる60度FOVをテストプレイ中だけ適用します。")]
    public bool overrideFieldOfView = true;
    [Range(20f, 90f)]
    public float fieldOfView = 60f;

    [Header("Obstacle avoidance (Unity alternative)")]
    public bool avoidObstacles = true;
    public LayerMask obstacleMask = ~0;
    [Min(0.01f)]
    public float collisionRadius = 0.25f;
    [Min(0f)]
    public float collisionPadding = 0.15f;
    [Min(0.1f)]
    public float minimumCameraDistance = 1.2f;

    [Header("CamEffect (original placement modes)")]
    [Tooltip("CamEffectの配置切替を有効にします。既存Inspector互換のためフィールド名を保持。")]
    public bool approximateCameraEffects = true;
    // Legacy Inspector fields retained for serialized compatibility; CamEffect no longer uses noise.
    [Min(0.01f)]
    public float cameraEffectDuration = 0.18f;
    [Min(0f)]
    public float cameraEffectPosition = 0.08f;
    [Min(0f)]
    public float cameraEffectRotation = 0.8f;
    [Min(0f)]
    public float cameraEffectFrequency = 32f;

    public bool cameraModeActive { get; private set; }
    public bool usingLockTarget { get; private set; }

    Vector3 savedPosition;
    Quaternion savedRotation;
    float savedFieldOfView;
    bool savedEditorCameraEnabled;
    bool savedEditorCameraCanMove;
    Vector3 followPosition;
    Quaternion followRotation;
    Vector3 positionVelocity;
    Vector3 previousPlayerPivot;
    bool hasPreviousPlayerPivot;
    Vector3 movementReferenceForward;
    bool hasMovementReference;
    public int OriginalShakeTicks { get; private set; }
    bool originalShakeNegative;
    float originalShakeOffset;
    public void RequestOriginalDistanceShake(Vector3 source, float radius)
    {
        // 004fa830 (62:1): truncate the scaled falloff, not the distance.
        RequestScaledShake(DistanceFromEye(source), radius, 3f);
    }
    float DistanceFromEye(Vector3 source)
    {
        Vector3 eye = controlledCamera != null ? controlledCamera.transform.position : transform.position;
        return Vector3.Distance(source, eye);
    }
    void RequestScaledShake(float distance, float radius, float scale)
    {
        float candidate = (radius - distance) * scale;
        if (distance >= 0f && distance < radius && OriginalShakeTicks < candidate)
            OriginalShakeTicks = (int)candidate;
    }
    void RequestQuantizedDistanceShake(float distance, int radius)
    {
        // 00464c80 / 004e3420 compare continuous falloff, then truncate distance.
        if (distance >= 0f && distance < radius && OriginalShakeTicks < (radius - distance) * 3f)
            OriginalShakeTicks = (radius - (int)distance) * 3;
    }
    public void RequestOriginalDownShake(Vector3 source)
    {
        RequestQuantizedDistanceShake(DistanceFromEye(source), 15);
    }
    public void RequestOriginalCannonShake(Vector3 source)
    {
        // 00468c40/00469260 (Canon), 004698c0/00469ea0 (Canon2).
        RequestScaledShake(DistanceFromEye(source), 30f, 1f);
    }
    public void RequestOriginalGrenadeShake(Vector3 source)
    {
        // 004957d0: one request when the fuse detonates, not on bounce/contact.
        RequestScaledShake(DistanceFromEye(source), 30f, 1.5f);
    }
    public void RequestOriginalOrdnanceImpactShake(Vector3 source)
    {
        // 0046d470 / 0046e9c0 / 0046f3c0: Missile3, Missile4 and Bomb contact.
        RequestScaledShake(DistanceFromEye(source), 30f, 1.5f);
    }
    public void RequestOriginalFireRunShake(Vector3 source)
    {
        // 00494ce0: caller gates this at pre-increment age % 3 == 0.
        RequestScaledShake(DistanceFromEye(source), 30f, 1f);
    }
    public void RequestOriginalSwordContactShake(Vector3 source)
    {
        // LZ_Sword (type32), hit callback 004830c0 only; not guard/terrain or Sword2.
        RequestScaledShake(DistanceFromEye(source), 20f, 1f);
    }
    public void RequestOriginalDefeatShake(Vector3 source)
    {
        // 004bf8a0: death counter31, using the live mech root position.
        RequestScaledShake(DistanceFromEye(source), 50f, 2.5f);
    }
    public void TickOriginalDistanceShake()
    {
        originalShakeOffset=0;
        if(OriginalShakeTicks<=0)return;
        if(OriginalShakeTicks%3==0)originalShakeNegative=!originalShakeNegative;
        originalShakeOffset=(originalShakeNegative?-1:1)*OriginalShakeTicks/200f;
        OriginalShakeTicks--;
    }
    public void RequestOriginalBeamShake(float distance)
    {
        RequestQuantizedDistanceShake(distance, 7);
    }
    readonly RaycastHit[] obstacleHits = new RaycastHit[16];

    void Awake()
    {
        ResolveReferences();
    }

    void OnDisable()
    {
        if (cameraModeActive)
            ExitTestPlayCamera();
    }

    void LateUpdate()
    {
        if (!cameraModeActive || controller == null || !controller.playModeActive || Time.timeScale == 0f)
            return;

        Transform playerRoot = GetPlayerRoot();
        if (playerRoot == null)
            return;

        UpdateFollowCamera(playerRoot, Time.deltaTime);
    }

    public void Bind(TestPlayController source)
    {
        controller = source;
    }

    public bool TryGetPlanarMovementBasis(out Vector3 forward, out Vector3 right)
    {
        forward = Vector3.zero;
        right = Vector3.zero;
        if (!cameraModeActive)
            return false;

        Transform playerRoot = GetPlayerRoot();
        if (playerRoot != null && TryBuildLogicalMovementForward(playerRoot, out Vector3 liveForward))
        {
            movementReferenceForward = liveForward;
            hasMovementReference = true;
        }
        if (!hasMovementReference)
            return false;

        forward = FlattenDirection(movementReferenceForward, Vector3.forward);
        right = Vector3.Cross(Vector3.up, forward).normalized;
        return right.sqrMagnitude > 0.000001f;
    }


    public void EnterTestPlayCamera(TestPlayController source)
    {
        ResolveReferences();
        Bind(source);
        if (controlledCamera == null || cameraModeActive)
            return;

        savedPosition = controlledCamera.transform.position;
        savedRotation = controlledCamera.transform.rotation;
        savedFieldOfView = controlledCamera.fieldOfView;
        savedEditorCameraEnabled = editorCamera != null && editorCamera.enabled;
        savedEditorCameraCanMove = editorCamera != null && editorCamera.canMove;

        if (editorCamera != null)
        {
            editorCamera.canMove = false;
            editorCamera.enabled = false;
        }

        cameraModeActive = true;
        usingLockTarget = false;
        positionVelocity = Vector3.zero;
        hasPreviousPlayerPivot = false;
        movementReferenceForward = Vector3.zero;
        hasMovementReference = false;
        OriginalShakeTicks = 0; originalShakeOffset = 0; originalShakeNegative = false;
        if (overrideFieldOfView)
            controlledCamera.fieldOfView = fieldOfView;

        Transform playerRoot = GetPlayerRoot();
        if (playerRoot != null)
            UpdateFollowCamera(playerRoot, 0f);
    }

    public void ExitTestPlayCamera()
    {
        if (!cameraModeActive)
            return;

        cameraModeActive = false;
        usingLockTarget = false;
        OriginalShakeTicks = 0; originalShakeOffset = 0; originalShakeNegative = false;
        positionVelocity = Vector3.zero;
        hasPreviousPlayerPivot = false;
        movementReferenceForward = Vector3.zero;
        hasMovementReference = false;

        if (controlledCamera != null)
        {
            controlledCamera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            controlledCamera.fieldOfView = savedFieldOfView;
        }

        if (editorCamera != null)
        {
            editorCamera.mousePosition = Input.mousePosition;
            editorCamera.canMove = savedEditorCameraCanMove;
            editorCamera.enabled = savedEditorCameraEnabled;
        }
    }

    void UpdateFollowCamera(Transform playerRoot, float deltaTime)
    {
        if (controlledCamera == null)
            return;

        Vector3 playerPivot = playerRoot.position + Vector3.up * playerPivotHeight;
        Transform lockTarget = GetLockTarget();
        usingLockTarget = lockTarget != null;

        Vector3 horizontalForward = FlattenDirection(playerRoot.forward, controlledCamera.transform.forward);
        Vector3 aimPoint;
        float distance = Mathf.Max(0.1f, followDistance);
        float height = followHeight;
        if (usingLockTarget)
        {
            Vector3 targetPivot = lockTarget.position + Vector3.up * targetPivotHeight;
            Vector3 targetForward = FlattenDirection(targetPivot - playerPivot, horizontalForward);
            horizontalForward = GetLockedOrbitForward(playerRoot, targetForward);
            float targetDistance = Vector3.Distance(playerPivot, targetPivot);
            distance += Mathf.Min(Mathf.Max(0f, maxTargetDistanceBonus), targetDistance * Mathf.Max(0f, targetDistanceScale));
            aimPoint = Vector3.Lerp(playerPivot, targetPivot, Mathf.Clamp01(targetFraming));
        }
        else
        {
            aimPoint = playerPivot + horizontalForward * unlockedLookAhead;
        }

        float backwardWeight = GetBackwardFollowWeight();
        distance += Mathf.Max(0f, backwardDistanceBonus) * backwardWeight;
        height += Mathf.Max(0f, backwardHeightBonus) * backwardWeight;

        Vector3 horizontalRight = Vector3.Cross(Vector3.up, horizontalForward).normalized;
        Vector3 desiredPosition = playerPivot - horizontalForward * distance + Vector3.up * height + horizontalRight * shoulderOffset;
        desiredPosition = ResolveObstaclePosition(playerPivot, desiredPosition, playerRoot, lockTarget);

        Vector3 lookDirection = aimPoint - desiredPosition;
        if (lookDirection.sqrMagnitude < 0.000001f)
            lookDirection = horizontalForward;
        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);

        bool snap = deltaTime <= 0f || !hasPreviousPlayerPivot;
        if (!snap && teleportSnapDistance > 0f)
            snap = Vector3.Distance(previousPlayerPivot, playerPivot) >= teleportSnapDistance;

        if (snap)
        {
            followPosition = desiredPosition;
            followRotation = desiredRotation;
            positionVelocity = Vector3.zero;
        }
        else
        {
            followPosition = Vector3.SmoothDamp(
                followPosition,
                desiredPosition,
                ref positionVelocity,
                Mathf.Max(0.001f, positionSmoothTime),
                Mathf.Infinity,
                deltaTime);
            float rotationT = rotationSharpness <= 0f ? 1f : 1f - Mathf.Exp(-rotationSharpness * deltaTime);
            followRotation = Quaternion.Slerp(followRotation, desiredRotation, rotationT);
        }

        previousPlayerPivot = playerPivot;
        hasPreviousPlayerPivot = true;
        // 入力基準は視覚補間済みのfollowRotationではなく、このフレームの論理配置方向を使う。
        // これによりカメラのSmoothDamp/Slerpが自機の旋回入力へ1フレーム遅れて帰還しない。
        movementReferenceForward = horizontalForward;
        hasMovementReference = true;
        ApplyCameraEffect(deltaTime);
    }

    bool TryBuildLogicalMovementForward(Transform playerRoot, out Vector3 forward)
    {
        forward = Vector3.zero;
        if (playerRoot == null)
            return false;

        forward = FlattenDirection(playerRoot.forward, Vector3.forward);
        Transform lockTarget = GetLockTarget();
        if (lockTarget != null)
        {
            Vector3 playerPivot = playerRoot.position + Vector3.up * playerPivotHeight;
            Vector3 targetPivot = lockTarget.position + Vector3.up * targetPivotHeight;
            Vector3 targetForward = FlattenDirection(targetPivot - playerPivot, forward);
            forward = GetLockedOrbitForward(playerRoot, targetForward);
        }

        return forward.sqrMagnitude > 0.000001f;
    }

    Vector3 GetLockedOrbitForward(Transform playerRoot, Vector3 targetForward)
    {
        if (!followLateralMovementHeading || controller == null || controller.state == null)
            return targetForward;

        int direction = controller.state.GetInt(190);
        if (!HasLateralInput(direction))
            return targetForward;

        Vector3 movementForward = FlattenDirection(playerRoot.forward, targetForward);
        float weight = Mathf.Clamp01(lateralMovementHeadingWeight);
        float signedAngle = Vector3.SignedAngle(targetForward, movementForward, Vector3.up);
        float orbitAngle = Mathf.Clamp(
            signedAngle * weight,
            -Mathf.Clamp(maxLockedOrbitAngle, 0f, 90f),
            Mathf.Clamp(maxLockedOrbitAngle, 0f, 90f));
        return FlattenDirection(Quaternion.AngleAxis(orbitAngle, Vector3.up) * targetForward, targetForward);
    }

    static bool HasLateralInput(int direction)
    {
        return direction == 1 || direction == 3 || direction == 4 ||
               direction == 6 || direction == 7 || direction == 9;
    }

    float GetBackwardFollowWeight()
    {
        if (!followBackwardMovement || controller == null || controller.state == null)
            return 0f;

        switch (controller.state.GetInt(190))
        {
            case 2:
                return 1f;
            case 1:
            case 3:
                return 0.7071068f;
            default:
                return 0f;
        }
    }

    Vector3 ResolveObstaclePosition(Vector3 pivot, Vector3 desiredPosition, Transform playerRoot, Transform lockTarget)
    {
        if (!avoidObstacles)
            return desiredPosition;

        Vector3 offset = desiredPosition - pivot;
        float distance = offset.magnitude;
        if (distance <= 0.0001f)
            return desiredPosition;

        int hitCount = Physics.SphereCastNonAlloc(
            pivot,
            Mathf.Max(0.01f, collisionRadius),
            offset / distance,
            obstacleHits,
            distance,
            obstacleMask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = distance;
        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = obstacleHits[i].collider != null ? obstacleHits[i].collider.transform : null;
            if (hitTransform == null || IsPartOf(hitTransform, playerRoot) || IsPartOf(hitTransform, lockTarget))
                continue;
            nearestDistance = Mathf.Min(nearestDistance, obstacleHits[i].distance);
        }

        if (nearestDistance >= distance)
            return desiredPosition;

        float resolvedDistance = Mathf.Max(minimumCameraDistance, nearestDistance - collisionPadding);
        resolvedDistance = Mathf.Min(resolvedDistance, distance);
        return pivot + offset.normalized * resolvedDistance;
    }

    void ApplyCameraEffect(float deltaTime)
    {
        Vector3 position = followPosition;
        Quaternion rotation = followRotation;
        if (approximateCameraEffects && controller != null && GetPlayerRoot() != null)
        {
            Transform root = GetPlayerRoot();
            if (TryCalculateCameraEffectPose(controller.CameraEffectMode,
                controller.CameraEffectPivot, new Pose(root.position, root.rotation),
                controller.CameraEffectTick, controller.state, out Pose pose))
            {
                position = pose.position;
                rotation = pose.rotation;
            }
        }
        // 004ccc2c: distance shake is applied after placement, independently of CamEffect=0.
        position.y += originalShakeOffset;
        controlledCamera.transform.SetPositionAndRotation(position, rotation);
    }

    // 004c9730 normal camera branch. D3DX row-vector products are reversed for Unity
    // column vectors. No follow smoothing, collision correction or LookAt is added to 4/5.
    public static bool TryCalculateCameraEffectPose(int mode, Vector3 pivot, Pose body,
        int globalTick, TestPlayStateTable state, out Pose pose)
    {
        pose = body;
        if (mode == 1 || mode == 2)
        {
            pose.position = pivot + body.rotation * new Vector3(mode == 1 ? 5f : -5f, 0f, 3f);
            pose.rotation = Quaternion.LookRotation(pivot - pose.position, Vector3.up);
            return true;
        }
        if (mode == 3)
        {
            float yaw = (unchecked((uint)globalTick) % 1080) * 0.3333f * 0.017453f * Mathf.Rad2Deg;
            pose.rotation = Quaternion.AngleAxis(yaw, Vector3.up)
                * Quaternion.AngleAxis(0.4363250136375427f * Mathf.Rad2Deg, Vector3.right);
            pose.position = pivot + pose.rotation * new Vector3(0f, 0f, -7f);
            return true;
        }
        if ((mode != 4 && mode != 5) || state == null) return false;
        const float degrees = 0.017453f * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.AngleAxis(state.GetFloat(107) * degrees, Vector3.forward)
            * Quaternion.AngleAxis(state.GetFloat(106) * degrees, Vector3.up)
            * Quaternion.AngleAxis(state.GetFloat(105) * degrees, Vector3.right);
        Vector3 translation = new Vector3(state.GetFloat(109), state.GetFloat(110), state.GetFloat(111));
        Vector3 distance = new Vector3(0f, 0f, -state.GetFloat(108));
        Vector3 local = mode == 4 ? rotation * (translation + distance) : rotation * distance + translation;
        pose.position = body.position + body.rotation * local;
        pose.rotation = body.rotation * rotation;
        return true;
    }

    void ResolveReferences()
    {
        if (controlledCamera == null)
            controlledCamera = GetComponent<Camera>();
        if (controlledCamera == null)
            controlledCamera = Camera.main;
        if (editorCamera == null && controlledCamera != null)
            editorCamera = controlledCamera.GetComponent<FreeCam>();
    }

    Transform GetPlayerRoot()
    {
        return controller != null && controller.robo != null && controller.robo.root != null
            ? controller.robo.root.transform
            : null;
    }

    Transform GetLockTarget()
    {
        if (!useLockTargetWhenAvailable || controller == null)
            return null;
        return controller.GetLockedTargetTransform();
    }

    static Vector3 FlattenDirection(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.000001f)
        {
            fallback.y = 0f;
            direction = fallback.sqrMagnitude > 0.000001f ? fallback : Vector3.forward;
        }
        return direction.normalized;
    }

    static bool IsPartOf(Transform candidate, Transform root)
    {
        return root != null && (candidate == root || candidate.IsChildOf(root));
    }
}
