using System.Globalization;
using Npgsql;

namespace PsikoAl.Api.E2ETests;

/// <summary>Kurulum/temizlik için doğrudan SQL (API'nin sunmadığı durumlar: bakiye sıfırlama, zaman kaydırma).</summary>
internal static class E2ESql
{
    public static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public static async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
    }

    public static async Task<string?> ScalarStringAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(LocalStack.PostgresConnectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (await command.ExecuteScalarAsync(CancellationToken.None))?.ToString();
    }
}
