#ifndef DUO_CURTAIN_GAMEPLAY_ADAPTIVE_CONTRAST_INCLUDED
#define DUO_CURTAIN_GAMEPLAY_ADAPTIVE_CONTRAST_INCLUDED

float DuoCurtainLuminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 DuoCurtainAdaptiveContrast(
    float3 sourceColor,
    float3 backgroundColor,
    float3 primaryColor,
    float3 secondaryColor,
    float contrastStrength,
    float contrastCurve,
    float brightnessBias,
    float adaptiveBlend,
    float priority,
    float edgeFactor,
    out float backgroundLuminance,
    out float contrastMap)
{
    backgroundLuminance = saturate(DuoCurtainLuminance(backgroundColor) + brightnessBias);
    float curve = max(0.1, contrastCurve);
    contrastMap = smoothstep(0.0, 1.0, pow(backgroundLuminance, curve));

    float priorityStrength = lerp(0.55, 1.0, saturate(priority / 100.0));
    float strength = saturate(contrastStrength * priorityStrength + edgeFactor);
    float3 brightVariant = lerp(sourceColor, max(sourceColor, primaryColor), strength);
    float3 darkVariant = lerp(sourceColor, min(sourceColor, secondaryColor), strength);
    float3 adaptiveColor = lerp(brightVariant, darkVariant, contrastMap);
    return lerp(sourceColor, adaptiveColor, saturate(adaptiveBlend));
}

#endif
