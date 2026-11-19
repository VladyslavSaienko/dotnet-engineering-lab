using LastCopy.Shared;
using LastCopy.V0.Naive;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddInventoryDatabase(builder.Configuration);
builder.Services.AddSingleton<IPurchaseService, NaivePurchaseService>();

var app = builder.Build();

await app.Services.GetRequiredService<InventoryDatabase>().EnsureCreatedAsync();

// Controllers live in LastCopy.Shared and are discovered automatically as an application part.
app.MapControllers();

await app.RunAsync();
