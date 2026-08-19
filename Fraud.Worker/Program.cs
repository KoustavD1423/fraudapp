using Fraud.Application;
using Fraud.Application.Data;
using Fraud.Application.Interfaces;
using Fraud.Application.Rules;
using Fraud.Contracts;
using Fraud.Infrastructure;
using Fraud.Worker;
using Fraud.Worker.Rules;
using Fraud.Worker.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Fraud.Application.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Fraud.Worker")
    .WriteTo.Console(
        outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] " +
        "[CorrelationId: {CorrelationId}] " +
        "[TransactionId: {TransactionId}] " +
        "[MessageId: {MessageId}] " +
        "{Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Fraud Worker Service...");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            var configuration = context.Configuration;

            // 1. Database Configuration
            services.AddDbContext<ApplicationDbContext>(opt =>
                opt.UseSqlServer(configuration.GetConnectionString("DefaultConnection")
                                 ??
                                 "Server=localhost\\SQLEXPRESS;Database=FraudDb;Trusted_Connection=True;TrustServerCertificate=True"));

            // 2. Singletons & Infrastructure
            services.AddSingleton<RedisService>();

            // 3. Fraud Rules (Registered as IFraudRule)
            services.AddScoped<IFraudRule, HighAmountRule>();
            services.AddScoped<IFraudRule, VeryHighAmountRule>();
            services.AddScoped<IFraudRule, VelocityRule>();


            // 4. Core Business Logic
            services.AddScoped<RuleEngine>();

            services.AddScoped<
                IFraudProcessingService,
                FraudProcessingService>();

            // 5. MassTransit & RabbitMQ Setup
            services.AddMassTransit(cfg =>
            {
                cfg.AddConsumer<TransactionConsumer>();

                cfg.UsingRabbitMq((busContext, busCfg) =>
                {
                    busCfg.Host("localhost", "/", h =>
                    {
                        h.Username("guest");
                        h.Password("guest");
                    });

                    busCfg.ReceiveEndpoint("transaction-queue", ep =>
                    {
                        ep.UseMessageRetry(r => { r.Interval(3, TimeSpan.FromSeconds(5)); });

                        ep.ConfigureConsumer<TransactionConsumer>(busContext);
                    });
                });
            });
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}