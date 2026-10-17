namespace Pds.Core;

public enum MeditationState { Ready, Running, Completed }

// The clock is sampled only when the view needs it; no background polling is needed.
public sealed class MeditationSession(TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private long started;
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);
    public MeditationState State { get; private set; }

    public void Start() { started = clock.GetTimestamp(); State = MeditationState.Running; }
    public TimeSpan Remaining
    {
        get
        {
            if (State == MeditationState.Ready) return Duration;
            if (State == MeditationState.Completed) return TimeSpan.Zero;
            var remaining = Duration - clock.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) return remaining;
            State = MeditationState.Completed;
            return TimeSpan.Zero;
        }
    }
    public void Close() => State = MeditationState.Ready;
}
