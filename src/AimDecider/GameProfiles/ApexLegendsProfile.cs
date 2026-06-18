namespace AimDecider.GameProfiles;

// Apex Legends is Source-engine-derived: at in-game sensitivity 1.0, m_yaw is
// 0.022 degrees of view rotation per raw mouse count (shared with CS:GO/CS2).
// Source: mouse-sensitivity.com's Apex Legends converter; Liquipedia Apex
// Legends "Mouse settings" page.
public sealed class ApexLegendsProfile : IGameProfile
{
    public const double MYaw = 0.022;

    public string Name => "Apex Legends";

    public double RecommendSensitivity(double targetDegreesPerCount) => targetDegreesPerCount / MYaw;
}
