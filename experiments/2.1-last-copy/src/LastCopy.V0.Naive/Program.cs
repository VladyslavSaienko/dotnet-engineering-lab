using LastCopy.Shared;
using LastCopy.Shared.Telemetry;
using LastCopy.V0.Naive;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddInventoryDatabase(builder.Configuration);
builder.Services.AddInstrumentedPurchaseService<NaivePurchaseService>();

var app = builder.Build();

await app.Services.GetRequiredService<InventoryDatabase>().EnsureCreatedAsync();

// Controllers live in LastCopy.Shared and are discovered automatically as an application part.
app.MapControllers();
app.MapDefaultEndpoints();

await app.RunAsync();
