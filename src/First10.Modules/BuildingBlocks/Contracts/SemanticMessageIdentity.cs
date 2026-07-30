namespace First10.Modules.BuildingBlocks.Contracts;

public readonly record struct SemanticMessageIdentity
{
    private SemanticMessageIdentity(string scope, string key)
    {
        Scope = scope;
        Key = key;
    }

    public string Scope { get; }

    public string Key { get; }

    public static SemanticMessageIdentity Create(string scope, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return new SemanticMessageIdentity(scope.Trim().ToLowerInvariant(), key.Trim());
    }

    public override string ToString() => $"{Scope}:{Key}";
}
