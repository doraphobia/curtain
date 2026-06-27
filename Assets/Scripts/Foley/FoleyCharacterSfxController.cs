using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FoleyCharacterSfxController : MonoBehaviour
{
    [Serializable]
    public class SfxSlot
    {
        public string id = "Slot";
        public FoleyProfile profile;
        public string surfaceIdOverride;
        [Range(0f, 1f)]
        public float volume = 1f;
        [Min(0f)]
        public float delay = 0f;
        public bool scaleDelayWithStepClock = true;
    }

    [Header("References")]
    public FoleyPlayer foleyPlayer;
    public FoleyStepClock stepClock;
    public Transform playFrom;

    [Header("Slots")]
    public List<SfxSlot> slots = new List<SfxSlot>
    {
        new SfxSlot { id = "LowClothing1" },
        new SfxSlot { id = "LowClothing2" },
        new SfxSlot { id = "UpperClothing1" },
        new SfxSlot { id = "UpperClothing2" },
        new SfxSlot { id = "LowEquipment1" },
        new SfxSlot { id = "LowEquipment2" },
        new SfxSlot { id = "UpperEquipment1" },
        new SfxSlot { id = "UpperEquipment2" },
        new SfxSlot { id = "WetLayer" }
    };

    private readonly Dictionary<string, SfxSlot> slotMap = new Dictionary<string, SfxSlot>(StringComparer.OrdinalIgnoreCase);

    void Awake()
    {
        ResolveReferences();
        RebuildSlotMap();
    }

    void OnValidate()
    {
        RebuildSlotMap();
    }

    public void LowClothingSound1() { PlaySlot("LowClothing1"); }
    public void LowClothingSound2() { PlaySlot("LowClothing2"); }
    public void UpperClothingSound1() { PlaySlot("UpperClothing1"); }
    public void UpperClothingSound2() { PlaySlot("UpperClothing2"); }
    public void LowEquipmentSound1() { PlaySlot("LowEquipment1"); }
    public void LowEquipmentSound2() { PlaySlot("LowEquipment2"); }
    public void UpperEquipmentSound1() { PlaySlot("UpperEquipment1"); }
    public void UpperEquipmentSound2() { PlaySlot("UpperEquipment2"); }
    public void WetLayer() { PlaySlot("WetLayer"); }

    public void PlaySlot(string id)
    {
        ResolveReferences();
        if (foleyPlayer == null || string.IsNullOrWhiteSpace(id))
            return;

        if (!slotMap.TryGetValue(id.Trim(), out SfxSlot slot) || slot == null || slot.profile == null)
            return;

        float delay = slot.delay;
        if (slot.scaleDelayWithStepClock && stepClock != null)
            delay *= Mathf.Clamp01(stepClock.LastStepData.delayMultiplier);

        if (delay > 0f)
            StartCoroutine(PlaySlotDelayed(slot, delay));
        else
            PlaySlotNow(slot);
    }

    private IEnumerator PlaySlotDelayed(SfxSlot slot, float delay)
    {
        yield return new WaitForSeconds(delay);
        PlaySlotNow(slot);
    }

    private void PlaySlotNow(SfxSlot slot)
    {
        if (slot == null || slot.profile == null || foleyPlayer == null)
            return;

        Vector3 position = playFrom != null ? playFrom.position : transform.position;
        string surfaceId = string.IsNullOrWhiteSpace(slot.surfaceIdOverride) ? null : slot.surfaceIdOverride;
        float pitchMultiplier = 1f;
        float delayMultiplier = 1f;
        if (stepClock != null)
        {
            FoleyStepClock.StepData stepData = stepClock.LastStepData;
            if (stepData.pitchMultiplier > 0f)
                pitchMultiplier = stepData.pitchMultiplier;
            if (stepData.delayMultiplier > 0f)
                delayMultiplier = stepData.delayMultiplier;
        }

        foleyPlayer.Play(slot.profile, position, slot.volume, surfaceId, pitchMultiplier, delayMultiplier);
    }

    private void ResolveReferences()
    {
        if (foleyPlayer == null)
            foleyPlayer = GetComponent<FoleyPlayer>();

        if (stepClock == null)
            stepClock = GetComponent<FoleyStepClock>();

        if (playFrom == null)
            playFrom = transform;
    }

    private void RebuildSlotMap()
    {
        slotMap.Clear();
        if (slots == null)
            return;

        for (int i = 0; i < slots.Count; i++)
        {
            SfxSlot slot = slots[i];
            if (slot == null || string.IsNullOrWhiteSpace(slot.id))
                continue;

            slotMap[slot.id.Trim()] = slot;
        }
    }
}
