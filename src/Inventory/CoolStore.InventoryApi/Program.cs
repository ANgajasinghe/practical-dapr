using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CoolStore.InventoryApi.Domain;
using CoolStore.InventoryApi.Infrastructure.Apis.GraphQL;
using CoolStore.InventoryApi.Infrastructure.Apis.Grpc;
using CoolStore.InventoryApi.Infrastructure.Persistence;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Playground;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using N8T.Infrastructure;
using N8T.Infrastructure.Dapr;
using N8T.Infrastructure.Data;
using N8T.Infrastructure.GraphQL;
using N8T.Infrastructure.Grpc;
using N8T.Infrastructure.Kestrel;
using N8T.Infrastructure.OTel;
using N8T.Infrastructure.Validator;
using OpenTelemetry.Trace;
using OpenTelemetry.Trace.Samplers;

Activity.DefaultIdFormat = ActivityIdFormat.W3C;

var (builder, config) = WebApplication.CreateBuilder(args)
    .AddCustomConfiguration();

var appOptions = config.GetOptions<AppOptions>("app");
if (!appOptions.NoTye.Enabled)
{
    builder.Host
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.ConfigureKestrel(o => o.ListenHttpAndGrpcProtocols(config));
        });
}

Console.WriteLine(Figgle.FiggleFonts.Doom.Render($"{appOptions.Name}"));

builder.Services
    .AddHttpContextAccessor()
    .AddCustomMediatR<Store>()
    .AddCustomValidators<Store>()
    .AddCustomDbContext<InventoryDbContext, Store>(config.GetConnectionString(Consts.SQLSERVER_DB_ID))
    .AddCustomMvc<Store>(withDapr: true)
    .AddCustomGraphQL(c =>
    {
        c.RegisterQueryType<QueryType>();
        c.RegisterObjectTypes(typeof(Store));
    })
    .AddCustomGrpc()
    .AddCustomDaprClient()
    .AddOpenTelemetry(b => b
        .SetSampler(new AlwaysOnSampler())
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddSqlClientDependencyInstrumentation()
        .AddMediatRInstrumentation()
        .UseZipkinExporter(o =>
        {
            o.ServiceName = "inventory-api";
            o.Endpoint = new Uri($"http://{config.GetServiceUri("zipkin")?.DnsSafeHost}:9411/api/v2/spans");
        })
    );

var app = builder.Build();

app.UseStaticFiles()
    .UseGraphQL("/graphql")
    .UsePlayground(new PlaygroundOptions {QueryPath = "/graphql", Path = "/playground"})
    .UseRouting()
    .UseCloudEvents()
    .UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
        endpoints.MapSubscribeHandler();
        endpoints.MapGrpcService<InventoryService>();
        endpoints.MapGet("/", context =>
        {
            context.Response.Redirect("/playground");
            return Task.CompletedTask;
        });
    });

await app.RunAsync();
