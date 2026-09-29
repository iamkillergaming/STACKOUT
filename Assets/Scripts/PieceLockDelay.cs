/// <summary>Counts grounded time, with a limited number of movement resets per piece.</summary>
public sealed class PieceLockDelay
{
    private float elapsed;
    private int resets;

    public void Reset()
    {
        elapsed = 0f;
        resets = 0;
    }

    public void OnSuccessfulAction(bool wasGrounded, int resetLimit)
    {
        if (!wasGrounded || resets >= resetLimit) return;
        elapsed = 0f;
        resets++;
    }

    public bool Tick(bool grounded, float deltaTime, float delay)
    {
        // Leaving a ledge pauses time but doesn't replenish the reset budget.
        if (!grounded) return false;
        elapsed += deltaTime;
        return elapsed >= delay;
    }
}
