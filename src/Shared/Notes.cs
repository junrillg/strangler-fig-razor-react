using Npgsql;
using NpgsqlTypes;

namespace Strangler.Common;

public sealed class Notes(NpgsqlDataSource database)
{
    public async Task<PageResult<Note>> ListAsync(Guid owner, Guid? after, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        await using var command = database.CreateCommand("""
            SELECT id, body, created_at FROM monolith.notes
            WHERE owner_id = $1 AND ($2::uuid IS NULL OR id > $2)
            ORDER BY id LIMIT $3
            """);
        command.Parameters.AddWithValue(owner);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)after ?? DBNull.Value });
        command.Parameters.AddWithValue(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<Note>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }

        var next = items.Count > limit ? items[limit - 1].Id : (Guid?)null;
        return new PageResult<Note>(items.Take(limit).ToArray(), next);
    }

    public async Task<Note?> CreateAsync(Guid owner, Guid requestId, string body, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            INSERT INTO monolith.notes (id, owner_id, request_id, body) VALUES ($1, $2, $3, $4)
            ON CONFLICT (owner_id, request_id) DO NOTHING
            RETURNING id, body, created_at
            """);
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(owner);
        command.Parameters.AddWithValue(requestId);
        command.Parameters.AddWithValue(body);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken)) { return Read(reader); }
        }

        await using var replay = database.CreateCommand("""
            SELECT id, body, created_at FROM monolith.notes
            WHERE owner_id = $1 AND request_id = $2 AND body = $3
            """);
        replay.Parameters.AddWithValue(owner);
        replay.Parameters.AddWithValue(requestId);
        replay.Parameters.AddWithValue(body);
        await using var existing = await replay.ExecuteReaderAsync(cancellationToken);
        return await existing.ReadAsync(cancellationToken) ? Read(existing) : null;
    }

    private static Note Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2));
}
