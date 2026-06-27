using UnityEngine;

[DisallowMultipleComponent]
public class FoleyAnimationEventBridge : MonoBehaviour
{
    [Header("References")]
    public FoleyStepClock stepClock;
    public FoleyPlayer foleyPlayer;
    public FoleyProfile footstepProfile;
    public FoleyCharacterSfxController characterSfx;
    public Transform eventTransform;
    public string surfaceIdOverride;

    [Header("Movement Gates")]
    [Min(0f)]
    public float walkMinSpeed = 0.1f;
    [Min(0f)]
    public float runMinSpeed = 8f;
    [Range(0f, 1f)]
    public float volume = 1f;

    void Awake()
    {
        ResolveReferences();
    }

    public void FootstepLeft(string movementType)
    {
        TriggerFootstep(movementType, FoleyStepClock.Foot.Left);
    }

    public void FootstepRight(string movementType)
    {
        TriggerFootstep(movementType, FoleyStepClock.Foot.Right);
    }

    public void Footstep(string movementType)
    {
        TriggerFootstep(movementType, null);
    }

    public void ClothingLow1()
    {
        if (characterSfx != null)
            characterSfx.LowClothingSound1();
    }

    public void ClothingLow2()
    {
        if (characterSfx != null)
            characterSfx.LowClothingSound2();
    }

    public void ClothingUpper1()
    {
        if (characterSfx != null)
            characterSfx.UpperClothingSound1();
    }

    public void ClothingUpper2()
    {
        if (characterSfx != null)
            characterSfx.UpperClothingSound2();
    }

    public void EquipmentLow1()
    {
        if (characterSfx != null)
            characterSfx.LowEquipmentSound1();
    }

    public void EquipmentLow2()
    {
        if (characterSfx != null)
            characterSfx.LowEquipmentSound2();
    }

    public void EquipmentUpper1()
    {
        if (characterSfx != null)
            characterSfx.UpperEquipmentSound1();
    }

    public void EquipmentUpper2()
    {
        if (characterSfx != null)
            characterSfx.UpperEquipmentSound2();
    }

    private void TriggerFootstep(string movementType, FoleyStepClock.Foot? foot)
    {
        ResolveReferences();
        if (stepClock == null || foleyPlayer == null || footstepProfile == null)
            return;

        float speed = stepClock.CurrentSpeed;
        if (!PassesMovementGate(movementType, speed))
            return;

        Vector3 position = eventTransform != null ? eventTransform.position : transform.position;
        stepClock.ForceStep(speed, position, foot, out FoleyStepClock.StepData stepData);
        string surfaceId = string.IsNullOrWhiteSpace(surfaceIdOverride) ? null : surfaceIdOverride;
        foleyPlayer.Play(
            footstepProfile,
            position,
            volume * stepData.volumeMultiplier,
            surfaceId,
            stepData.pitchMultiplier,
            stepData.delayMultiplier
        );
    }

    private bool PassesMovementGate(string movementType, float speed)
    {
        if (string.IsNullOrWhiteSpace(movementType))
            return speed >= walkMinSpeed;

        if (movementType.Equals("Run", System.StringComparison.OrdinalIgnoreCase))
            return speed >= runMinSpeed;

        if (movementType.Equals("Walk", System.StringComparison.OrdinalIgnoreCase))
            return speed >= walkMinSpeed && speed < runMinSpeed;

        return speed >= walkMinSpeed;
    }

    private void ResolveReferences()
    {
        if (stepClock == null)
            stepClock = GetComponent<FoleyStepClock>();

        if (foleyPlayer == null)
            foleyPlayer = GetComponent<FoleyPlayer>();

        if (characterSfx == null)
            characterSfx = GetComponent<FoleyCharacterSfxController>();

        if (eventTransform == null)
            eventTransform = transform;
    }
}
