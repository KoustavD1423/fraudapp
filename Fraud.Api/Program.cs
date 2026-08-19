using Fraud.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Fraud.Application;
using Fraud.Api.GraphQL;
using MassTransit;
using Fraud.Contracts;
//using Fraud.Application.Events
using Fraud.Application.Events;
using Fraud.Application.Factories;
using Fraud.Application.Context;
using Fraud.Api.Services;
using Fraud.Application.Data;
using Fraud.Application.Interfaces;
using Fraud.Application.Services;
using Serilog;
using Fraud.Api.Middleware;
using Fraud.Application.Configuration;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var databaseOptions = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>();

builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseSqlServer(databaseOptions!.ConnectionString));
//builder.Services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IEventBus, RabbitMqEventBus>();
builder.Services.AddMassTransit(cfg =>
{
    cfg.UsingRabbitMq((context, busCfg) =>
    {
        var rabbitOptions = context.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;

        busCfg.Host(
            rabbitOptions.Host,
            rabbitOptions.VirtualHost,
            h =>
            {
                h.Username(rabbitOptions.Username);
                h.Password(rabbitOptions.Password);
            });
    });
});

// REQUIRED for MassTransit v7
//builder.Services.AddMassTransitHostedService();

// Services
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<ITransactionEventFactory, TransactionEventFactory>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<
    ITransactionContextProvider,
    TransactionContextProvider>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

builder.Services
    .AddGraphQLServer()
    .ModifyRequestOptions(opt => opt.IncludeExceptionDetails = true)
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.Configure<RedisOptions>(
    builder.Configuration.GetSection(RedisOptions.SectionName));


Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Fraud.Api")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapGraphQL();

app.Run();