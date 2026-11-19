var builder = DistributedApplication.CreateBuilder(args);

// Persistent container: SQL Server keeps running between AppHost restarts, so runs start faster.
var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent);

var inventory = sql.AddDatabase("inventory");

// LastCopy__Variant becomes the "variant" tag on purchase metrics and traces.
builder.AddProject<Projects.LastCopy_V0_Naive>("v0-naive")
    .WithEnvironment("LastCopy__Variant", "v0-naive")
    .WithReference(inventory)
    .WaitFor(inventory);

builder.Build().Run();
