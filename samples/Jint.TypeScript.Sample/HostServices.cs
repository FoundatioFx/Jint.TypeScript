namespace Jint.TypeScript.Sample;

/// <summary>The C# object exposed to TypeScript as the global "host".</summary>
public sealed class HostServices(string requestId, Action<string> log)
{
    public string RequestId { get; } = requestId;
    public string ReceivedAt { get; } = DateTimeOffset.UtcNow.ToString("O");

    public void Log(string message) => log(message);
}
