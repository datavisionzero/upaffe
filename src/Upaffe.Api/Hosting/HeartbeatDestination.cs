namespace Upaffe.Api.Hosting;

/// <summary>A receiver URL kept out of diagnostic rendering.</summary>
public sealed class HeartbeatDestination
{
    public static HeartbeatDestination Disabled { get; } = new(null);

    public Uri? Url { get; }

    public HeartbeatDestination(Uri? url) => Url = url;

    public override string ToString() => Url is null ? "disabled" : "configured (redacted)";
}
