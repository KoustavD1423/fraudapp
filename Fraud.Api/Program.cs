using Fraud.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Fraud.Application;
using Fraud.Api.GraphQL;
using MassTransit;
using Fraud.Contracts;
//using Fraud.Application.Events
using Fraud.Application.Events;


using Fraud.Application.Data;
using Fraud.Application.Interfaces;
using Fraud.Application.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
//builder.Services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IEventBus, RabbitMqEventBus>();
builder.Services.AddMassTransit(cfg =>
{
    cfg.UsingRabbitMq((context, busCfg) =>
    {
        busCfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
    });
});

// REQUIRED for MassTransit v7
//builder.Services.AddMassTransitHostedService();

// Services
builder.Services.AddScoped<TransactionService>();

builder.Services
    .AddGraphQLServer()
    .ModifyRequestOptions(opt => opt.IncludeExceptionDetails = true)
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();



Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

app.MapGraphQL();

app.Run();