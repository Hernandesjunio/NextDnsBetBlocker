namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;

/// <summary>
/// Adaptador de logger para ParallelBatchManager
/// Usa o logger genérico de ListImportConsumer
/// </summary>
internal class ParallelBatchManagerLogger : ILogger<ParallelBatchManager>
{
    private readonly ILogger _innerLogger;

    public ParallelBatchManagerLogger(ILogger innerLogger)
    {
        _innerLogger = innerLogger;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _innerLogger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _innerLogger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _innerLogger.Log(logLevel, eventId, state, exception, formatter);
    }
}
