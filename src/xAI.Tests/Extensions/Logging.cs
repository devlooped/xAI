using Microsoft.Extensions.Logging;

public static class LoggerFactoryExtensions
{
    public static ILoggerFactory AsLoggerFactory(this ITestOutputHelper output) => new TestLoggerFactory(output);

    public static ILoggingBuilder AddTestOutput(this ILoggingBuilder builder, ITestOutputHelper output)
        => builder.AddProvider(new TestLoggerProider(output));

    class TestLoggerProider(ITestOutputHelper output) : ILoggerProvider
    {
        readonly ILoggerFactory factory = new TestLoggerFactory(output);

        public ILogger CreateLogger(string categoryName) => factory.CreateLogger(categoryName);

        public void Dispose() { }
    }

    class TestLoggerFactory(ITestOutputHelper output) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new TestOutputLogger(output, categoryName);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        // create ilogger implementation over testoutputhelper
        public class TestOutputLogger(ITestOutputHelper output, string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (formatter == null) throw new ArgumentNullException(nameof(formatter));
                if (state == null) throw new ArgumentNullException(nameof(state));
                output.WriteLine($"{logLevel}: {categoryName}: {formatter(state, exception)}");
            }
        }
    }
}