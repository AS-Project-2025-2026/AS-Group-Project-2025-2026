using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Clients;
using Nop.IntegrationWorker.Data;
using Nop.IntegrationWorker.Messaging;
using Nop.IntegrationWorker.Options;
using Nop.IntegrationWorker.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var cfg = ctx.Configuration;

        services.Configure<RabbitMqOptions>(cfg.GetSection("RabbitMq"));
        services.Configure<WorkerOptions>(cfg.GetSection("Worker"));
        services.Configure<WarehouseOptions>(cfg.GetSection("Warehouse"));
        services.Configure<ShippingOptions>(cfg.GetSection("Shipping"));

        var connectionString = cfg["ConnectionStrings:ConnectionString"]
            ?? throw new InvalidOperationException("ConnectionStrings:ConnectionString is required");

        services.AddSingleton(_ => new WorkerDataService(connectionString));
        services.AddSingleton<RabbitMqConnectionFactory>();
        services.AddSingleton<RabbitMqPublisher>();

        services.AddHttpClient<WarehouseClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WarehouseOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient<ShippingClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<ShippingOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHostedService<OutboxPollingService>();
        services.AddHostedService<FulfillmentConsumerService>();
        services.AddHostedService<ShippingConsumerService>();
    })
    .Build();

await host.RunAsync();
