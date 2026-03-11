using System.Globalization;
using System.Text;
using IoT_AI_Demo.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IoT_AI_Demo.Orchestrator;

public sealed class AlarmEmbeddingService(
    NpgsqlDataSource db,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<AlarmEmbeddingService> logger)
{

    // ── Schema ──────────────────────────────────────────────────────

    public async Task EnsureSchemaAsync()
    {
        const string sql = """
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS alarm_embeddings (
                id BIGSERIAL PRIMARY KEY,
                device_id TEXT NOT NULL,
                alarm_level TEXT NOT NULL,
                root_cause TEXT NOT NULL,
                summary TEXT NOT NULL,
                adjusted_severity TEXT NOT NULL,
                value DOUBLE PRECISION NOT NULL,
                embedding vector(1536) NOT NULL,
                timestamp TIMESTAMPTZ NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_alarm_embeddings_hnsw
                ON alarm_embeddings USING hnsw (embedding vector_cosine_ops);
            """;

        await using var cmd = db.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();

        logger.LogInformation("Alarm embeddings schema ensured");
    }

    // ── Embedding generation ────────────────────────────────────────

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var result = await embeddingGenerator.GenerateAsync([text], cancellationToken: ct);
        return result[0].Vector.ToArray();
    }

    // ── Store ───────────────────────────────────────────────────────

    public async Task StoreAsync(
        AlarmMessage alarm,
        AiAnalysisResult analysis,
        float[] embedding)
    {
        const string sql = """
            INSERT INTO alarm_embeddings
                (device_id, alarm_level, root_cause, summary, adjusted_severity, value, embedding, timestamp)
            VALUES ($1, $2, $3, $4, $5, $6, $7::vector, $8)
            """;

        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(alarm.DeviceId);
        cmd.Parameters.AddWithValue(alarm.AlarmLevel.ToString());
        cmd.Parameters.AddWithValue(analysis.RootCause);
        cmd.Parameters.AddWithValue(analysis.Summary);
        cmd.Parameters.AddWithValue(analysis.AdjustedSeverity);
        cmd.Parameters.AddWithValue(alarm.Value);
        cmd.Parameters.AddWithValue(ToVectorLiteral(embedding));
        cmd.Parameters.AddWithValue(alarm.Timestamp);
        await cmd.ExecuteNonQueryAsync();

        logger.LogInformation("Stored alarm embedding for {DeviceId}", alarm.DeviceId);
    }

    // ── Similarity search ───────────────────────────────────────────

    public async Task<List<SimilarAlarmResult>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 5)
    {
        const string sql = """
            SELECT device_id, alarm_level, root_cause, summary,
                   1 - (embedding <=> $1::vector) AS similarity,
                   timestamp
            FROM alarm_embeddings
            ORDER BY embedding <=> $1::vector
            LIMIT $2
            """;

        var results = new List<SimilarAlarmResult>();
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(ToVectorLiteral(queryEmbedding));
        cmd.Parameters.AddWithValue(topK);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new SimilarAlarmResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDouble(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        logger.LogInformation("Found {Count} similar alarms", results.Count);
        return results;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    public static string BuildAlarmText(AlarmMessage alarm, AiAnalysisResult? analysis = null)
    {
        var sb = new StringBuilder();
        sb.Append($"Device: {alarm.DeviceId} ({alarm.DeviceType}). ");
        sb.Append($"Alarm: {alarm.AlarmLevel} — {alarm.Description}. ");
        sb.Append(CultureInfo.InvariantCulture, $"Value: {alarm.Value} {alarm.Unit}. ");
        if (analysis is not null)
        {
            sb.Append($"Root cause: {analysis.RootCause}. ");
            sb.Append($"Summary: {analysis.Summary}. ");
        }
        return sb.ToString();
    }

    /// <summary>Converts a float array to pgvector text literal format: [0.1,0.2,...]</summary>
    private static string ToVectorLiteral(float[] vector)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < vector.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString(CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

}
