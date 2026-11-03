using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Extensions.Logging;
using Nexstrap.Services.Logging;

namespace Nexstrap.Tests;

public class BindingErrorListenerTests
{
    private sealed class CollectingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    [Fact]
    public void Broken_bindings_reach_the_log()
    {
        var logger = new CollectingLogger();
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                BindingErrorListener.Attach(logger);
                var block = new TextBlock { DataContext = new { Real = 1 } };
                block.SetBinding(TextBlock.TextProperty, new Binding("DoesNotExist"));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Contains(logger.Messages, m => m.Contains("DoesNotExist"));
    }
}
