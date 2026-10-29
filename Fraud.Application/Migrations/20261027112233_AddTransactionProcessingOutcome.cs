using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fraud.Application.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionProcessingOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FraudReasons",
                table: "Transactions",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsFraud",
                table: "Transactions",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingStatus",
                table: "Transactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "RiskScore",
                table: "Transactions",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Transactions",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FraudReasons",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsFraud",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ProcessingStatus",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RiskScore",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Transactions");
        }
    }
}
