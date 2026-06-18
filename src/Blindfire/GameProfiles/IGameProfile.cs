namespace Blindfire.GameProfiles;

public interface IGameProfile
{
    string Name { get; }

    double RecommendSensitivity(double targetDegreesPerCount);
}
