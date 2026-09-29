/// <summary>
/// Contract for any entity that can receive experience points.
/// Decouples the XP source (enemy death) from the XP receiver (player progression).
/// Dependency Inversion: enemies fire an event; the player's XP system listens.
/// </summary>
public interface IXpReceiver
{
    /// <summary>Current accumulated XP.</summary>
    int CurrentXp { get; }

    /// <summary>Adds the given amount of experience points to the receiver.</summary>
    void AddXp(int amount);
}
