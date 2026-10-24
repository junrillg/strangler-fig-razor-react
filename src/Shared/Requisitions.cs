using Npgsql;
using NpgsqlTypes;

namespace Strangler.Common;

public sealed class Requisitions(NpgsqlDataSource database)
{
    public static readonly Guid DesignatedApprover = new("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private const string Columns = "id, requester_id, approver_id, title, reason, status, version, created_at, updated_at";

    public async Task<PageResult<Requisition>> ListAsync(Guid participant, Guid? after, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        await using var command = database.CreateCommand($"""
            SELECT {Columns} FROM monolith.requisitions
            WHERE (requester_id = $1 OR approver_id = $1) AND ($2::uuid IS NULL OR id > $2)
            ORDER BY id LIMIT $3
            """);
        command.Parameters.AddWithValue(participant);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)after ?? DBNull.Value });
        command.Parameters.AddWithValue(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<Requisition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }

        var next = items.Count > limit ? items[limit - 1].Id : (Guid?)null;
        return new PageResult<Requisition>(items.Take(limit).ToArray(), next);
    }

    public async Task<Requisition?> GetAsync(Guid participant, Guid id, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand($"""
            SELECT {Columns} FROM monolith.requisitions
            WHERE (requester_id = $1 OR approver_id = $1) AND id = $2
            """);
        command.Parameters.AddWithValue(participant);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<Requisition?> CreateAsync(Guid requester, Guid requestId, string title, string reason, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            INSERT INTO monolith.requisitions (id, requester_id, approver_id, request_id, create_title, create_reason, title, reason)
            VALUES ($1, $2, $3, $4, $5, $6, $5, $6)
            ON CONFLICT (requester_id, request_id) DO NOTHING
            RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(requester);
        command.Parameters.AddWithValue(DesignatedApprover);
        command.Parameters.AddWithValue(requestId);
        command.Parameters.AddWithValue(title);
        command.Parameters.AddWithValue(reason);
        Requisition? created;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            created = await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }

        if (created is not null)
        {
            await AppendAuditAsync(connection, transaction, created, requester, null, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created;
        }

        await using var existing = new NpgsqlCommand($"""
            SELECT {Columns} FROM monolith.requisitions
            WHERE requester_id = $1 AND request_id = $2 AND create_title = $3 AND create_reason = $4
            """, connection, transaction);
        existing.Parameters.AddWithValue(requester);
        existing.Parameters.AddWithValue(requestId);
        existing.Parameters.AddWithValue(title);
        existing.Parameters.AddWithValue(reason);
        Requisition? replay;
        await using (var reader = await existing.ExecuteReaderAsync(cancellationToken))
        {
            replay = await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }

        await transaction.CommitAsync(cancellationToken);
        return replay;
    }

    public async Task<ChangeResult> UpdateDraftAsync(Guid requester, Guid id, string title, string reason, int version, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand($"""
            UPDATE monolith.requisitions
            SET title = $3, reason = $4, version = version + 1, updated_at = now()
            WHERE requester_id = $1 AND id = $2 AND version = $5 AND status = 'draft'
            RETURNING {Columns}
            """);
        command.Parameters.AddWithValue(requester);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(title);
        command.Parameters.AddWithValue(reason);
        command.Parameters.AddWithValue(version);
        Requisition? updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            updated = await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }

        return updated is not null ? new ChangeResult(updated, null)
            : await FailureAsync(requester, id, version, cancellationToken);
    }

    public async Task<ChangeResult> TransitionAsync(Guid actor, Guid id, bool deciding, string target, string? reason, int version, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var actorColumn = deciding ? "approver_id" : "requester_id";
        var source = deciding ? "submitted" : "draft";
        await using var command = new NpgsqlCommand($"""
            UPDATE monolith.requisitions SET status = $3, version = version + 1, updated_at = now()
            WHERE {actorColumn} = $1 AND id = $2 AND status = $4 AND version = $5
            RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(target);
        command.Parameters.AddWithValue(source);
        command.Parameters.AddWithValue(version);
        Requisition? updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            updated = await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }

        if (updated is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            await connection.CloseAsync();
            return await FailureAsync(actor, id, version, cancellationToken);
        }

        await AppendAuditAsync(connection, transaction, updated, actor, source, reason, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChangeResult(updated, null);
    }

    public async Task<IReadOnlyList<AuditEntry>> AuditAsync(Guid participant, Guid id, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT a.id, a.actor_id, a.from_status, a.to_status, a.version, a.reason, a.occurred_at
            FROM monolith.requisition_audit a JOIN monolith.requisitions r ON r.id = a.requisition_id
            WHERE r.id = $2 AND (r.requester_id = $1 OR r.approver_id = $1)
            ORDER BY a.version LIMIT 4
            """);
        command.Parameters.AddWithValue(participant);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<AuditEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new AuditEntry(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetDateTime(6)));
        }

        return entries;
    }

    private async Task<ChangeResult> FailureAsync(Guid participant, Guid id, int version, CancellationToken cancellationToken)
    {
        var current = await GetAsync(participant, id, cancellationToken);
        return new ChangeResult(null, current is null ? "not_found"
            : current.Version != version ? "version_conflict" : "invalid_transition");
    }

    private static async Task AppendAuditAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Requisition item, Guid actor, string? source, string? reason, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO monolith.requisition_audit (id, requisition_id, actor_id, from_status, to_status, version, reason)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            """, connection, transaction);
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(item.Id);
        command.Parameters.AddWithValue(actor);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)source ?? DBNull.Value });
        command.Parameters.AddWithValue(item.Status);
        command.Parameters.AddWithValue(item.Version);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)reason ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Requisition Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetDateTime(7), reader.GetDateTime(8));
}
