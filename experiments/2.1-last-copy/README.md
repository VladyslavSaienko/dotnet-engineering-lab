# Experiment 2.1 — The last copy

What is being measured and why: [SPEC.md](SPEC.md).

## Run the experiment

Requires the .NET SDK from `global.json` and a running Docker engine.

```bash
dotnet run --project experiments/2.1-last-copy/src/LastCopy.AppHost
```

The AppHost starts SQL Server (persistent container) and the variants, then prints the dashboard URL with a login token:

```
Login to the dashboard at https://localhost:<port>/login?t=<token>
```

Send requests with [LastCopy.V0.Naive.http](src/LastCopy.V0.Naive/LastCopy.V0.Naive.http). Set `baseUrl` to the `v0-naive` endpoint shown on the dashboard's **Resources** page (the http endpoint from the project's launch settings, `http://localhost:53643`).

### HTTPS development certificate

The dashboard receives telemetry over HTTPS. If the ASP.NET Core development certificate is not trusted, the services cannot export to it and the dashboard stays empty (the dashboard shows a "No trusted development certificate" banner). Trust it once:

```bash
dotnet dev-certs https --trust
```

## Where to see the telemetry

Open the dashboard and pick the `v0-naive` resource.

- **Metrics** → meter `LastCopy.Purchases`:
  - `lastcopy.purchases`: purchase attempts. Use the filters to split by `outcome` (`succeeded`, `insufficient_stock`, `product_not_found`) and `variant`.
  - `lastcopy.purchase.duration`: P50/P90/P99 in seconds, same tags.
  - Also available: `http.server.request.duration` (all requests including 400s), `dotnet.*` runtime metrics, `http.client.*`.
- **Traces**: one trace per request, `POST purchases` → `lastcopy.purchase` (tags `product_id`, `quantity`, `variant`, `outcome`) → SqlClient spans (`SELECT dbo.Products`, `UPDATE`, `INSERT dbo.Purchases`). Health probes are not traced.
- **Structured logs**: application logs, correlated with traces.

Metrics are exported once per OpenTelemetry export interval (60 seconds by default), so new values can take up to a minute to appear. Traces appear within seconds.

Metric names, units and tags are listed in [SPEC.md](SPEC.md#telemetry).
