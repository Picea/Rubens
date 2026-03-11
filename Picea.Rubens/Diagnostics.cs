// =============================================================================
// Actor Diagnostics — OpenTelemetry-compatible Tracing
// =============================================================================

using System.Diagnostics;

namespace Picea.Rubens;

/// <summary>
/// OpenTelemetry diagnostics for the actor subsystem.
/// </summary>
internal static class ActorDiagnostics
{
    public const string SourceName = "Picea.Rubens";
    public static readonly ActivitySource Source = new(SourceName);
}
