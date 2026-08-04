namespace Unifi.Mcp.Client;

internal readonly struct SecretValue
{
    private readonly string _value;

    public SecretValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public string Reveal() => _value;

    public override string ToString() => "[redacted]";
}
