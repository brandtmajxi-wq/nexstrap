using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Logging;

/// <summary>
/// Sends WPF data-binding errors (normally only visible in a debugger) to the log file, so broken
/// bindings show up in QA runs and in logs users send in.
/// </summary>
public sealed class BindingErrorListener(ILogger logger) : TraceListener
{
    public static void Attach(ILogger logger)
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener(logger));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
    }

    public override void Write(string? message) { }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            logger.LogWarning("Binding error: {Message}", message);
    }
}
