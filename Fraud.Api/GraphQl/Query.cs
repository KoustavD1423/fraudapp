
using Fraud.Domain;
using Fraud.Application;
using Microsoft.EntityFrameworkCore;
using Fraud.Infrastructure;
using Fraud.Application.Data;
namespace Fraud.Api.GraphQL;

public class Query
{
    public IQueryable<Transaction> GetTransactions([Service] ApplicationDbContext db) => db.Transactions;
    public IQueryable<Alert> GetAlerts([Service] ApplicationDbContext db) => db.Alerts;
}