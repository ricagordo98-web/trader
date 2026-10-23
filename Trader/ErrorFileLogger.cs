using Microsoft.Extensions.Logging;

namespace Trader;

public sealed class ErrorFileLoggerProvider : ILoggerProvider
{
    private static readonly object Gate = new();
    private readonly string _path = Path.Combine(FileSystem.AppDataDirectory, "errors.log");

    public ILogger CreateLogger(string categoryName) => new ErrorFileLogger(categoryName, _path);

    public void Dispose()
    {
    }

    private sealed class ErrorFileLogger(string category, string path) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                lock (Gate)
                {
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{category}] {formatter(state, exception)}{Environment.NewLine}{exception}{Environment.NewLine}");
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
