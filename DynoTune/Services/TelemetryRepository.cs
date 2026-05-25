using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;

namespace DynoTune.Services;

public sealed record TelemetrySample(
    DateTime TimestampUtc,
    double CpuUsagePct,
    double GpuUsagePct,
    bool IsOptimizing);

public sealed class TelemetryRepository : IDisposable
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynoTune", "telemetry.db");

    private readonly SqliteConnection? _conn;

    public TelemetryRepository()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            _conn = new SqliteConnection($"Data Source={DbPath}");
            _conn.Open();
            Execute("PRAGMA journal_mode=WAL;");
            Execute("""
                CREATE TABLE IF NOT EXISTS TelemetrySamples (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc TEXT    NOT NULL,
                    CpuUsagePct  REAL    NOT NULL,
                    GpuUsagePct  REAL    NOT NULL,
                    IsOptimizing INTEGER NOT NULL
                );
                """);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TelemetrySamples WHERE TimestampUtc < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", DateTime.UtcNow.AddHours(-24).ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { Debug.WriteLine($"[TelemetryRepo] Init: {ex.Message}"); }
    }

    public void Insert(DateTime timestampUtc, double cpuPct, double gpuPct, bool isOptimizing)
    {
        if (_conn?.State != System.Data.ConnectionState.Open) return;
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO TelemetrySamples (TimestampUtc, CpuUsagePct, GpuUsagePct, IsOptimizing)
                VALUES (@ts, @cpu, @gpu, @opt);
                """;
            cmd.Parameters.AddWithValue("@ts",  timestampUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@cpu", cpuPct);
            cmd.Parameters.AddWithValue("@gpu", gpuPct);
            cmd.Parameters.AddWithValue("@opt", isOptimizing ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { Debug.WriteLine($"[TelemetryRepo] Insert: {ex.Message}"); }
    }

    /// <summary>Returns up to <paramref name="maxCount"/> most-recent samples, oldest first.</summary>
    public IReadOnlyList<TelemetrySample> GetRecent(int maxCount)
    {
        var result = new List<TelemetrySample>();
        if (_conn?.State != System.Data.ConnectionState.Open) return result;
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT TimestampUtc, CpuUsagePct, GpuUsagePct, IsOptimizing
                FROM TelemetrySamples
                ORDER BY Id DESC LIMIT @n;
                """;
            cmd.Parameters.AddWithValue("@n", maxCount);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new TelemetrySample(
                    DateTime.Parse(reader.GetString(0),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.GetDouble(1), reader.GetDouble(2), reader.GetInt32(3) != 0));
            result.Reverse(); // newest-first → oldest-first
        }
        catch (Exception ex) { Debug.WriteLine($"[TelemetryRepo] GetRecent: {ex.Message}"); }
        return result;
    }

    public void Dispose() { try { _conn?.Dispose(); } catch { } }

    private void Execute(string sql)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
