namespace ShowCast.Core;

public enum TimerType { Counter, Clock }

public class TimerDef
{
    public Guid      Id             { get; init; } = Guid.NewGuid();
    public string    Name           { get; set; }  = "Timer";
    public TimerType Type           { get; set; }  = TimerType.Counter;

    // Counter: counts between two values (seconds)
    public int  StartSeconds    { get; set; } = 300;   // default 5:00
    public int  EndSeconds      { get; set; } = 0;

    // Clock: countdown to a specific wall-clock time (HH:mm, 24-hour)
    public string ClockTime     { get; set; } = "12:00";

    // Warning when approaching end
    public bool WarnEnabled     { get; set; } = true;
    public int  WarnOffset      { get; set; } = 30;    // seconds before end

    // Continue counting past end (negative overflow)
    public bool OverflowEnabled { get; set; } = false;
}
