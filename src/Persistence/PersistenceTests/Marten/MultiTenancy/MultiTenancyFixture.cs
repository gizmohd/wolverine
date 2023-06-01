using IntegrationTests;
using Marten;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Oakton.Resources;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Wolverine;
using Wolverine.Marten;
using Wolverine.RDBMS.MultiTenancy;
using Wolverine.Runtime;
using Wolverine.Tracking;
using Xunit;

namespace PersistenceTests.Marten.MultiTenancy;

public class MultiTenancyContext : IClassFixture<MultiTenancyFixture>
{
    public MultiTenancyFixture Fixture { get; }

    public MultiTenancyContext(MultiTenancyFixture fixture)
    {
        Fixture = fixture;
        Runtime = fixture.Host.GetRuntime();
        Databases = Runtime.Storage.ShouldBeOfType<MultiTenantedMessageDatabase>();
    }

    public MultiTenantedMessageDatabase Databases { get; }

    public WolverineRuntime Runtime { get; }
}


public class MultiTenancyFixture : IAsyncLifetime
{
    public IHost? Host { get; private set; }
    
    private async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(Servers.PostgresConnectionString);

        var exists = await conn.DatabaseExists(databaseName);
        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;

        return builder.ConnectionString;
    }

    public async Task RestartAsync()
    {
        if (Host != null)
        {
            await Host.StopAsync();
        }

        await InitializeAsync();
    }


    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(Servers.PostgresConnectionString);
        await conn.OpenAsync();
        
        var tenant1ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant1");
        var tenant2ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant2");
        var tenant3ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant3");


        Host = await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.Services.AddMarten(m =>
                {
                    m.DatabaseSchemaName = "mt";
                    m.MultiTenantedDatabases(tenancy =>
                    {
                        tenancy.AddSingleTenantDatabase("tenant1", tenant1ConnectionString);
                        tenancy.AddSingleTenantDatabase("tenant2", tenant2ConnectionString);
                        tenancy.AddSingleTenantDatabase("tenant3", tenant3ConnectionString);
                    });
                    
                }).IntegrateWithWolverine(schemaName:"control", masterDatabaseConnectionString:Servers.PostgresConnectionString);

                opts.Services.AddResourceSetupOnStartup();
            }).StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            await Host.StopAsync();
        }
    }
}