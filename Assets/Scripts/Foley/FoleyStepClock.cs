using System;
using UnityEngine;

[DisallowMultipleComponent]
public class FoleyStepClock : MonoBehaviour
{
    public enum Foot
    {
        Left,
        Right
    }

    [Serializable]
    public struct StepData
    {
        public int index;
        public Foot foot;
        public Vector3 worldPosition;
        public float speed;
        public float normalizedSpeed;
        public float secondsPerStep;
        public float stepsPerSecond;
        public float volumeMultiplier;
        public float pitchMultiplier;
        public float heelToeDelay;
        public float delayMultiplier;
        public bool isRunning;
    }

    [Header("Cadence")]
    [Min(0.01f)]
    public float distancePerStep = 1.2f;
    [Min(0f)]
    public float minSecondsBetweenSteps = 0.08f;
    [Min(0.01f)]
    public float maxSecondsBetweenSteps = 0.65f;
    [Min(0f)]
    public float minMovingSpeed = 0.05f;
    [Min(0.01f)]
    public float speedForFullCadence = 16f;
    [Min(0f)]
    public float runSpeedThreshold = 8f;

    [Header("Heel Toe Automation")]
    [Min(0f)]
    public float slowHeelToeDelay = 0.18f;
    [Min(0f)]
    public float fastHeelToeDelay = 0f;

    [Header("Speed Modulation")]
    [Range(0f, 2f)]
    public float slowVolumeMultiplier = 0.65f;
    [Range(0f, 2f)]
    public float fastVolumeMultiplier = 1f;
    [Range(0.1f, 3f)]
    public float slowPitchMultiplier = 0.96f;
    [Range(0.1f, 3f)]
    public float fastPitchMultiplier = 1.04f;

    [Header("Idle")]
    public bool resetWhenIdle = true;
    [Min(0f)]
    public float idleSecondsBeforeReset = 0.35f;

    private float distanceAccumulator;
    private float clockTime;
    private float nextStepTime;
    private float idleTimer;
    private int stepIndex;
    private StepData lastStepData;

    public float CurrentSpeed { get; private set; }
    public float CurrentNormalizedSpeed { get; private set; }
    public float CurrentSecondsPerStep { get; private set; } = 0.5f;
    public float CurrentStepsPerSecond { get; private set; }
    public float StepPhase => Mathf.Clamp01(distanceAccumulator / Mathf.Max(0.01f, distancePerStep));
    public StepData LastStepData => lastStepData;

    public bool Tick(float movedDistance, float speed, float deltaTime, Vector3 worldPosition, out StepData stepData)
    {
        clockTime += Mathf.Max(0f, deltaTime);
        CurrentSpeed = Mathf.Max(0f, speed);
        CurrentNormalizedSpeed = Mathf.InverseLerp(minMovingSpeed, Mathf.Max(minMovingSpeed + 0.01f, speedForFullCadence), CurrentSpeed);
        CurrentSecondsPerStep = ComputeSecondsPerStep(CurrentSpeed);
        CurrentStepsPerSecond = CurrentSecondsPerStep > 0f ? 1f / CurrentSecondsPerStep : 0f;

        stepData = default(StepData);

        if (movedDistance <= 0.0001f || CurrentSpeed < minMovingSpeed)
        {
            idleTimer += Mathf.Max(0f, deltaTime);
            if (resetWhenIdle && idleTimer >= idleSecondsBeforeReset)
                ResetClock(false);

            return false;
        }

        idleTimer = 0f;
        distanceAccumulator += Mathf.Max(0f, movedDistance);

        if (distanceAccumulator < distancePerStep || clockTime < nextStepTime)
            return false;

        distanceAccumulator = Mathf.Min(distanceAccumulator - distancePerStep, distancePerStep * 0.5f);
        stepData = BuildStepData(worldPosition, null);
        lastStepData = stepData;
        nextStepTime = clockTime + stepData.secondsPerStep;
        stepIndex++;
        return true;
    }

    public bool ForceStep(float speed, Vector3 worldPosition, out StepData stepData)
    {
        return ForceStep(speed, worldPosition, null, out stepData);
    }

    public bool ForceStep(float speed, Vector3 worldPosition, Foot? forcedFoot, out StepData stepData)
    {
        clockTime += Time.deltaTime;
        CurrentSpeed = Mathf.Max(0f, speed);
        CurrentNormalizedSpeed = Mathf.InverseLerp(minMovingSpeed, Mathf.Max(minMovingSpeed + 0.01f, speedForFullCadence), CurrentSpeed);
        CurrentSecondsPerStep = ComputeSecondsPerStep(CurrentSpeed);
        CurrentStepsPerSecond = CurrentSecondsPerStep > 0f ? 1f / CurrentSecondsPerStep : 0f;

        stepData = BuildStepData(worldPosition, forcedFoot);
        lastStepData = stepData;
        distanceAccumulator = 0f;
        nextStepTime = clockTime + stepData.secondsPerStep;
        stepIndex++;
        return true;
    }

    public void ResetClock()
    {
        ResetClock(true);
    }

    public float GetAnimatorSpeedMultiplier(float referenceStepsPerSecond, float minMultiplier, float maxMultiplier)
    {
        if (CurrentSpeed < minMovingSpeed || CurrentStepsPerSecond <= 0f)
            return 1f;

        float reference = Mathf.Max(0.01f, referenceStepsPerSecond);
        return Mathf.Clamp(CurrentStepsPerSecond / reference, minMultiplier, maxMultiplier);
    }

    private void ResetClock(bool resetStepIndex)
    {
        distanceAccumulator = 0f;
        nextStepTime = clockTime;
        idleTimer = 0f;
        CurrentSpeed = 0f;
        CurrentNormalizedSpeed = 0f;
        CurrentStepsPerSecond = 0f;
        CurrentSecondsPerStep = maxSecondsBetweenSteps;
        if (resetStepIndex)
            stepIndex = 0;
    }

    private StepData BuildStepData(Vector3 worldPosition, Foot? forcedFoot)
    {
        float normalizedSpeed = Mathf.Clamp01(CurrentNormalizedSpeed);
        float heelToeDelay = Mathf.Lerp(slowHeelToeDelay, fastHeelToeDelay, normalizedSpeed);
        float baseDelay = Mathf.Max(0.0001f, slowHeelToeDelay);

        return new StepData
        {
            index = stepIndex,
            foot = forcedFoot ?? ((stepIndex % 2 == 0) ? Foot.Left : Foot.Right),
            worldPosition = worldPosition,
            speed = CurrentSpeed,
            normalizedSpeed = normalizedSpeed,
            secondsPerStep = CurrentSecondsPerStep,
            stepsPerSecond = CurrentStepsPerSecond,
            volumeMultiplier = Mathf.Lerp(slowVolumeMultiplier, fastVolumeMultiplier, normalizedSpeed),
            pitchMultiplier = Mathf.Lerp(slowPitchMultiplier, fastPitchMultiplier, normalizedSpeed),
            heelToeDelay = heelToeDelay,
            delayMultiplier = Mathf.Clamp01(heelToeDelay / baseDelay),
            isRunning = CurrentSpeed >= runSpeedThreshold
        };
    }

    private float ComputeSecondsPerStep(float speed)
    {
        if (speed < minMovingSpeed)
            return maxSecondsBetweenSteps;

        float seconds = distancePerStep / Mathf.Max(0.01f, speed);
        float minStep = Mathf.Max(0f, minSecondsBetweenSteps);
        float maxStep = Mathf.Max(minStep + 0.01f, maxSecondsBetweenSteps);
        return Mathf.Clamp(seconds, minStep, maxStep);
    }
}
