using Microsoft.Extensions.Options;

namespace PrintServer.Queue;

public sealed class RetryPolicy(IOptions<QueueOptions> options, TimeProvider time)
{
    private readonly QueueOptions _options = options.Value;

    public bool IsExhausted(int attempts) => attempts >= _options.MaxAttempts;

    public TimeSpan DelayAfter(int attempts) => _options.RetryBaseDelay * Math.Pow(2, attempts - 1);

    public DateTimeOffset NextAttemptAfter(int attempts) => time.GetUtcNow() + DelayAfter(attempts);
}
