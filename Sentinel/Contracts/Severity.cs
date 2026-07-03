namespace Contracts
{
    /// <summary>
    /// Severity level attached to a monitor event.
    /// </summary>
    public enum Severity
    {
        /// <summary>
        /// Normal information that helps explain the current state.
        /// </summary>
        Info,

        /// <summary>
        /// Suspicious behavior that should be visible to the professor but does not stop the system.
        /// </summary>
        Warning,

        /// <summary>
        /// Serious integrity offenses where the exam cannot be continued safely and might require exam invalidation.
        /// </summary>
        Critical
    }
}