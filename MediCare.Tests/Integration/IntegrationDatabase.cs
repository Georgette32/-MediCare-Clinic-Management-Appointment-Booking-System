using MediCare.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Tests.Integration;

/// <summary>
/// A disposable LocalDB database for one web host. It is created and migrated before the host starts (Program seeds the
/// bootstrap Admin at startup, which needs the schema) and dropped when the owning factory is disposed.
/// </summary>
public sealed class IntegrationDatabase : IDisposable
{
    private const string LocalDbInstance = "(localdb)\\MSSQLLocalDB";
    private readonly string _masterConnectionString;

    private IntegrationDatabase(string databaseName)
    {
        DatabaseName = databaseName;
        ConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = LocalDbInstance,
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 20,
            MultipleActiveResultSets = true
        }.ConnectionString;
        _masterConnectionString = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    /// <summary>Creates an empty database and applies every migration to it.</summary>
    public static IntegrationDatabase CreateAndMigrate()
    {
        var database = new IntegrationDatabase("MediCare_PageTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var master = new SqlConnection(database._masterConnectionString))
            {
                master.Open();
                using var create = master.CreateCommand();
                create.CommandText = $"CREATE DATABASE [{database.DatabaseName}]";
                create.ExecuteNonQuery();
            }

            using var context = new MediCareDbContext(
                new DbContextOptionsBuilder<MediCareDbContext>().UseSqlServer(database.ConnectionString).Options);
            context.Database.Migrate();
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        SqlConnection.ClearAllPools();
        using var master = new SqlConnection(_masterConnectionString);
        master.Open();
        using var drop = master.CreateCommand();
        drop.CommandText = $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END";
        drop.ExecuteNonQuery();
    }
}
