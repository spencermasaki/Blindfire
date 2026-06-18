namespace Blindfire.GameProfiles;

// Extensibility point for future games - only Apex Legends is implemented today.
public static class GameProfileRegistry
{
    public static IReadOnlyList<IGameProfile> All { get; } = new IGameProfile[]
    {
        new ApexLegendsProfile(),
    };
}
