using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateSQLite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerTenants",
                columns: table => new
                {
                    CustomerTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefaultDomainName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    MspId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<uint>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTenants", x => x.CustomerTenantId);
                });

            migrationBuilder.CreateTable(
                name: "TenantGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GraphGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    MailEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SecurityEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GroupTypes = table.Column<string>(type: "TEXT", nullable: true),
                    MspId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<uint>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GraphUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserPrincipalName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    AccountEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    JobTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Department = table.Column<string>(type: "TEXT", nullable: true),
                    MspId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<uint>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantUsers", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerTenants");

            migrationBuilder.DropTable(
                name: "TenantGroups");

            migrationBuilder.DropTable(
                name: "TenantUsers");
        }
    }
}
