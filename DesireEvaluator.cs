using UnityEngine;
using System.Collections.Generic;

public enum DesireType
{
    Survival,
    Vengeance,
    Regeneration,
    Territory,
    Attrition,
    ElementalAdvantage,
    Dominance,
    Recovery,
    CrystalDefense,
    None
}

public class DesireResult
{
    public DesireType StrongestDesire;
    public DesireType SecondChoice;
    public Transform TargetTransform;
    public Vector3 TargetPosition;
    public float Urgency; // 0 to 1
}

/// <summary>
/// A completely decoupled decision maker.
/// It runs a weighted desire table and returns a decision.
/// It holds no state and subscribes to no events. It only evaluates when called.
/// </summary>
public class DesireEvaluator : MonoBehaviour
{
    // Tuning Weights
    [Header("Base Desire Weights")]
    public float survivalWeight = 1f;
    public float vengeanceWeight = 1f;
    public float territoryWeight = 1f;

    [Header("Dynamic Tuning")]
    [Tooltip("How much random fuzziness to add to desire scores to prevent deterministic loops.")]
    public float fuzzyLogicRange = 0.2f;
    [Tooltip("Multiplier applied to aggressive desires for every missing body segment.")]
    public float enrageMultiplierPerLostSegment = 0.2f;
    [Tooltip("How long before a recently used desire fully recovers its priority.")]
    public float desireCooldownDuration = 10f;
    [Tooltip("How much to heavily penalize a desire if it was just used (spam prevention).")]
    public float recentUsePenalty = 0.8f; // Cuts the weight by 80% if spammed

    // We can expand the weights as we iterate on the AI.

    public DesireResult Evaluate(BossCreature brain, EnvironmentTagRegistry registry, DesireType lastDesire, float timeSinceLastDesire)
    {
        DesireResult result = new DesireResult();

        // 0. Calculate Global Modifiers
        float enrageBonus = 1.0f;
        SegmentedDragonManager anatomy = brain.GetComponent<SegmentedDragonManager>();
        if (anatomy != null && anatomy.originalSegmentCount > 0)
        {
            // Let's just use health percentage as an enrage modifier instead of segments to be safe.
            float healthPct = brain.GetCurrentHealthPct();
            enrageBonus = 1.0f + ((1.0f - healthPct) * 2.0f); // Up to 3x multiplier at 0% health
        }

        // Helper function for Fuzzy Logic + Anti-Spam
        float GetDynamicWeight(DesireType type, float baseWeight, bool isAggressive)
        {
            float weight = baseWeight;

            // Enrage multiplier for aggressive actions (Breath, Swoop)
            if (isAggressive) weight *= enrageBonus;

            // Anti-Spam: Diminishing returns if we just used this
            if (type == lastDesire && timeSinceLastDesire < desireCooldownDuration)
            {
                // Gradually recover the weight as time passes
                float recoveryRatio = timeSinceLastDesire / desireCooldownDuration;
                float penalty = recentUsePenalty * (1.0f - recoveryRatio);
                weight *= (1.0f - penalty);
            }

            // Fuzzy Logic: slight unpredictability
            weight += Random.Range(-fuzzyLogicRange, fuzzyLogicRange);
            return Mathf.Max(0, weight);
        }

        // --- 1. Evaluate Crystal Defense (Absolute Priority if active) ---
        // If the brain tells us a crystal is currently threatened (e.g. within the last 5 seconds)
        if (brain.LastThreatenedCrystal != null)
        {
            result.StrongestDesire = DesireType.CrystalDefense;
            result.SecondChoice = DesireType.Vengeance;
            result.TargetTransform = brain.LastThreatenedCrystal.transform;
            result.TargetPosition = brain.LastThreatenedCrystal.transform.position;
            result.Urgency = 1.0f; // Maximum urgency
            return result;
        }

        // --- 2. Evaluate Survival & Recovery (Health/Stamina driven) ---
        float currentHealthPct = brain.GetCurrentHealthPct();
        if (currentHealthPct < 0.3f)
        {
            result.StrongestDesire = DesireType.Survival;
            result.SecondChoice = DesireType.Regeneration;

            // Try to find a crystal to retreat to
            EnvironmentTag tag = registry.GetNearestTag(brain.transform.position, EnvironmentTag.TagType.ObservationLoop);
            if (tag != null)
            {
                result.TargetTransform = tag.transform;
                result.TargetPosition = tag.transform.position;
            }
            result.Urgency = 1f - currentHealthPct;
            return result;
        }

        // --- 3. Default to Dominance/Vengeance (Combat focus) ---
        // Instead of hardcoding, we run the weighted fuzzy logic to see what wins!
        float domWeight = GetDynamicWeight(DesireType.Dominance, 1.0f, true);
        float elemWeight = GetDynamicWeight(DesireType.ElementalAdvantage, 1.0f, true);
        float spawnWeight = GetDynamicWeight(DesireType.Attrition, 0.8f, false);
        float terrWeight = GetDynamicWeight(DesireType.Territory, 0.6f, false);

        // Find the winner
        float maxScore = domWeight;
        result.StrongestDesire = DesireType.Dominance;

        if (elemWeight > maxScore) { maxScore = elemWeight; result.StrongestDesire = DesireType.ElementalAdvantage; }
        if (spawnWeight > maxScore) { maxScore = spawnWeight; result.StrongestDesire = DesireType.Attrition; }
        if (terrWeight > maxScore) { maxScore = terrWeight; result.StrongestDesire = DesireType.Territory; }

        result.SecondChoice = DesireType.None;
        result.TargetPosition = brain.transform.position; // Fallback
        result.Urgency = 0.5f * enrageBonus; // Scales with enrage

        return result;
    }
}
