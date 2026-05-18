using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fraud.Application.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyConstraintToAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TransactionId",
                table: "Alerts",
                column: "TransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Alerts_TransactionId",
                table: "Alerts");
        }
    }
}
