using First10.Modules.BuildingBlocks.Contracts;

namespace First10.UnitTests.BuildingBlocks;

public sealed class IdempotencyTests
{
    [Fact]
    public void SameScopeAndKeyAreTheSameSemanticMessage()
    {
        var first = SemanticMessageIdentity.Create("telegram", "update-42");
        var replay = SemanticMessageIdentity.Create("telegram", "update-42");

        Assert.Equal(first, replay);
    }

    [Fact]
    public void DifferentScopesDoNotCollide()
    {
        var telegram = SemanticMessageIdentity.Create("telegram", "42");
        var whatsapp = SemanticMessageIdentity.Create("whatsapp", "42");

        Assert.NotEqual(telegram, whatsapp);
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData("channel", "")]
    [InlineData(" ", "message")]
    public void BlankIdentityPartsAreRejected(string scope, string key)
    {
        Assert.Throws<ArgumentException>(() => SemanticMessageIdentity.Create(scope, key));
    }
}
