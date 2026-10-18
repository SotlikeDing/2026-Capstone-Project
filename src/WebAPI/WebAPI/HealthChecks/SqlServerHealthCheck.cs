using Microsoft.Data.SqlClient;
using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WebAPI.HealthChecks;

/// <summary>ASP.NET Core health check that opens a fresh connection and runs SELECT 1, reporting round-trip time - backs AdminController.GetSystemHealth.</summary>
public class SqlServerHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public SqlServerHealthCheck(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = new SqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            sw.Stop();

            var data = new Dictionary<string, object> { ["responseTimeMs"] = sw.Elapsed.TotalMilliseconds };
            return HealthCheckResult.Healthy("SQL Server Pool Healthy", data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var data = new Dictionary<string, object> { ["responseTimeMs"] = sw.Elapsed.TotalMilliseconds };
            return HealthCheckResult.Unhealthy(ex.Message, ex, data);
        }
    }
}
