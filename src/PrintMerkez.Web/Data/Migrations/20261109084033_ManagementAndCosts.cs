using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintMerkez.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManagementAndCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_OccurredAtUtc",
                table: "Jobs");

            migrationBuilder.AddColumn<int>(
                name: "AppliedTariffId",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AppliedUnitKurus",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CostKurus",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostState",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentName",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserDisplayName",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Entity = table.Column<string>(type: "TEXT", nullable: false),
                    EntityKey = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tariffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MonochromeKurus = table.Column<int>(type: "INTEGER", nullable: false),
                    ColorKurus = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tariffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tariffs_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirectoryUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Account = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    NormalizedAccount = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectoryUsers_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Source_OccurredAtUtc",
                table: "Jobs",
                columns: new[] { "Source", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryUsers_DepartmentId",
                table: "DirectoryUsers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryUsers_NormalizedAccount",
                table: "DirectoryUsers",
                column: "NormalizedAccount",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tariffs_PrinterId_EffectiveFromUtc",
                table: "Tariffs",
                columns: new[] { "PrinterId", "EffectiveFromUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "DirectoryUsers");

            migrationBuilder.DropTable(
                name: "Tariffs");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_Source_OccurredAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AppliedTariffId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AppliedUnitKurus",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CostKurus",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CostState",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "DepartmentName",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "UserDisplayName",
                table: "Jobs");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_OccurredAtUtc",
                table: "Jobs",
                column: "OccurredAtUtc");
        }
    }
}
