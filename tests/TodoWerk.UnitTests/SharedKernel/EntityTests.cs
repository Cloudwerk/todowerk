using TodoWerk.SharedKernel;
using Xunit;

namespace TodoWerk.UnitTests.SharedKernel;

public sealed class EntityTests
{
    private sealed class TestEntity(Guid id) : Entity<Guid>(id);

    private sealed class OtherEntity(Guid id) : Entity<Guid>(id);

    [Fact]
    public void Entities_WithSameTypeAndId_AreEqual()
    {
        var id = Guid.NewGuid();

        var left = new TestEntity(id);
        var right = new TestEntity(id);

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Entities_WithDifferentIds_AreNotEqual()
    {
        var left = new TestEntity(Guid.NewGuid());
        var right = new TestEntity(Guid.NewGuid());

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Entities_OfDifferentTypes_AreNotEqual()
    {
        var id = Guid.NewGuid();

        var left = new TestEntity(id);
        var right = new OtherEntity(id);

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Entity_IsNotEqualToNull()
    {
        var entity = new TestEntity(Guid.NewGuid());

        Assert.False(entity.Equals(null));
    }
}
