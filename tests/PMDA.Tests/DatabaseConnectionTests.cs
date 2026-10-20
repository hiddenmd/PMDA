using PMDA.Data;

namespace PMDA.Tests;

public class DatabaseConnectionTests
{
    [Fact]
    public void Unset_connection_throws_a_clear_error()
    {
        var connection = new DatabaseConnection();
        Assert.False(connection.IsConfigured);
        Assert.Null(connection.Current);
        Assert.Throws<DatabaseNotConfiguredException>(() => connection.Value);
    }

    [Fact]
    public async Task Waiters_resume_when_a_connection_is_set_and_it_can_be_replaced()
    {
        var connection = new DatabaseConnection();
        var waiting = connection.WhenConfiguredAsync(CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        connection.Set("Server=a;Database=X");
        await waiting.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("Server=a;Database=X", connection.Value);

        connection.Set("Server=b;Database=Y");
        Assert.Equal("Server=b;Database=Y", connection.Value);
        Assert.True(connection.WhenConfiguredAsync(CancellationToken.None).IsCompleted);
    }

    [Fact]
    public void Initial_value_and_fixed_connection()
    {
        Assert.True(new DatabaseConnection("Server=a;Database=X").IsConfigured);
        Assert.False(new DatabaseConnection("  ").IsConfigured);
        Assert.Equal("cs", new FixedConnection("cs").Value);
    }
}
