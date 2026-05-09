using System;
using System.Collections.Generic;

namespace ShowCast.Core;

public enum RepeatType { None, Daily, Weekly, Monthly }

public class ScheduledEvent
{
    public Guid             Id             { get; init; } = Guid.NewGuid();
    public Guid             RundownId      { get; set; }
    public Guid             PackageId      { get; set; }
    public Guid?            PageId         { get; set; }
    public DateTime         ScheduledAt    { get; set; } = DateTime.Now;
    public string           Label          { get; set; } = string.Empty;
    public bool             IsEnabled      { get; set; } = true;
    public bool             HasRun         { get; set; } = false;
    public RepeatType       Repeat         { get; set; } = RepeatType.None;
    public int              RepeatInterval { get; set; } = 1;
    // Bitmask: bit 0=Sunday, 1=Monday, 2=Tuesday, 3=Wednesday, 4=Thursday, 5=Friday, 6=Saturday
    public int              RepeatWeekDays { get; set; } = 0;
    public DateTime?        RepeatUntil    { get; set; }
    public List<TimerAction> TimerActions  { get; set; } = new();
}
