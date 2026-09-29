using System;

public static class PieceLockDelayChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Main()
    {
        var timer = new PieceLockDelay();
        Check(!timer.Tick(false, 10f, 0.5f), "Airborne piece locked");
        Check(!timer.Tick(true, 0.25f, 0.5f), "Landing locked early");
        Check(timer.Tick(true, 0.25f, 0.5f), "Grounded piece failed to lock");
        timer.Reset();
        timer.Tick(true, 0.4f, 0.5f);
        timer.OnSuccessfulAction(true, 15);
        Check(!timer.Tick(true, 0.2f, 0.5f), "Grounded move failed to reset");
        Check(timer.Tick(true, 0.3f, 0.5f), "Reset timer failed to expire");
        timer.Reset();
        for (int i = 0; i < 15; i++)
        {
            Check(!timer.Tick(true, 0.4f, 0.5f), "Reset budget exhausted early");
            timer.OnSuccessfulAction(true, 15);
        }
        timer.Tick(true, 0.25f, 0.5f);
        timer.OnSuccessfulAction(true, 15);
        Check(timer.Tick(true, 0.25f, 0.5f), "Reset limit allowed endless stalling");
        timer.Reset();
        timer.Tick(true, 0.25f, 0.5f);
        timer.OnSuccessfulAction(false, 15);
        Check(!timer.Tick(false, 2f, 0.5f), "Airborne timer locked");
        Check(timer.Tick(true, 0.25f, 0.5f), "Airborne action replenished timer");
        timer.Reset();
        Check(!timer.Tick(true, 0.25f, 0.5f), "Spawn failed to reset timer");
        timer.OnSuccessfulAction(true, 0);
        Check(timer.Tick(true, 0.25f, 0.5f), "Zero reset limit ignored");
        Console.WriteLine("Lock delay checks passed: landing, resets, limit, airborne pause, spawn.");
    }
}
