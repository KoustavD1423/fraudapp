using Fraud.Application;
using Fraud.Application.Data;
using Fraud.Application.Interfaces;
using Fraud.Application.Rules;
using Fraud.Contracts;
using Fraud.Infrastructure;
using Fraud.Worker;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Fraud.Contracts;
using Fraud.Application.Rules;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog() // ✅ Placed here correctly!
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<ApplicationDbContext>(opt =>
            opt.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=FraudDb;Trusted_Connection=True;TrustServerCertificate=True"));


        services.AddScoped<IFraudRule, HighAmountRule>();
        services.AddScoped<IFraudRule, VeryHighAmountRule>();

        services.AddScoped<RuleEngine>();
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
                    ep.UseMessageRetry(r =>
                    {
                        r.Interval(3, TimeSpan.FromSeconds(5)); 
                    });

                    ep.ConfigureConsumer<TransactionConsumer>(busContext);
                });
            });
        });
    })
    .Build();

// 🔥 KEEP WORKER ALIVE
await host.RunAsync();