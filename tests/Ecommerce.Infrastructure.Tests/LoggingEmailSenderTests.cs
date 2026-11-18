using Ecommerce.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Infrastructure.Tests;

public sealed class LoggingEmailSenderTests
{
    private sealed class CapturingLogger : ILogger<LoggingEmailSender>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    [Fact]
    public async Task TC_RESET_011_the_development_sender_never_logs_the_address_link_or_token()
    {
        var logger = new CapturingLogger();
        const string token = "raw-token-value";

        await new LoggingEmailSender(logger).SendPasswordResetAsync("john@example.test", "https://reset.test/?token=" + token, CancellationToken.None);

        Assert.NotEmpty(logger.Lines);
        Assert.All(logger.Lines, line =>
        {
            Assert.DoesNotContain(token, line);
            Assert.DoesNotContain("john@example.test", line);
        });
    }
}
