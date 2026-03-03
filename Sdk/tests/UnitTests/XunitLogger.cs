using Microsoft.Extensions.Logging;

namespace UnitTests;

/// <summary>
/// bridges <see cref="ILogger"/> to xUnit's <see cref="ITestOutputHelper"/>
/// so SDK log output appears in test results.
/// </summary>
internal class XunitLogger(ITestOutputHelper output) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception != null)
            output.WriteLine(exception.ToString());
    }
}
