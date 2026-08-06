namespace ShutdownGuard.Services;

/// <summary>
/// Why the scheduler entered today's reminder window.
/// Used for accurate logging — never claim resume without a real power signal.
/// </summary>
internal enum ReminderWindowEntryReason
{
    /// <summary>Armed for a future ReminderStart; clock reached it on schedule.</summary>
    Scheduled,

    /// <summary>Start() while already inside [ReminderStart, 22:00).</summary>
    Startup,

    /// <summary>
    /// Was waiting for a future ReminderStart, but when the loop woke
    /// the clock had jumped past it while still inside the window.
    /// (Could be sleep/resume or system time change — we do not claim which.)
    /// </summary>
    TimeAdvance,

    /// <summary>UpdatePlan re-armed into an already-active reminder window.</summary>
    PlanUpdate
}
