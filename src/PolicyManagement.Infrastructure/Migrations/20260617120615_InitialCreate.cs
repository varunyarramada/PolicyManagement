using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_number = table.Column<string>(type: "varchar(20)", nullable: false),
                    policyholder_name = table.Column<string>(type: "nvarchar(200)", nullable: false),
                    line_of_business = table.Column<string>(type: "varchar(50)", nullable: false),
                    status = table.Column<string>(type: "varchar(50)", nullable: false),
                    premium_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "varchar(10)", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    region = table.Column<string>(type: "varchar(100)", nullable: false),
                    underwriter = table.Column<string>(type: "nvarchar(200)", nullable: false),
                    flagged_for_review = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Policies_CreatedAt",
                table: "Policies",
                column: "created_at",
                descending: new bool[0],
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_EffectiveDate",
                table: "Policies",
                column: "effective_date",
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_ExpiryDate",
                table: "Policies",
                column: "expiry_date",
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_ExpiryDate_Status",
                table: "Policies",
                columns: new[] { "expiry_date", "status" },
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_FlaggedForReview",
                table: "Policies",
                column: "flagged_for_review",
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_LineOfBusiness",
                table: "Policies",
                column: "line_of_business",
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_PolicyholderName",
                table: "Policies",
                column: "policyholder_name",
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_Region",
                table: "Policies",
                column: "region",
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_Status",
                table: "Policies",
                column: "status",
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_Status_LineOfBusiness",
                table: "Policies",
                columns: new[] { "status", "line_of_business" },
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_Status_Region",
                table: "Policies",
                columns: new[] { "status", "region" },
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "UQ_Policies_PolicyNumber",
                table: "Policies",
                column: "policy_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Policies");
        }
    }
}
