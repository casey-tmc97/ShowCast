using System;

namespace ShowCast.Core;

public enum TimerActionType { Play, Pause, Reset, PlayPause }

public class TimerAction
{
    public Guid            Id     { get; init; } = Guid.NewGuid();
    public Guid            TimerId { get; set; }
    public TimerActionType Action  { get; set; } = TimerActionType.Play;
}
