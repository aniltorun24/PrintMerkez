using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintMerkez.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeviceCopyEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_PrinterId",
                table: "Jobs");

            migrationBuilder.AddColumn<DateTime>(
                name: "CountVerifiedAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceJobKey",
                table: "Jobs",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_PrinterId_DeviceJobKey",
                table: "Jobs",
                columns: new[] { "PrinterId", "DeviceJobKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_PrinterId_DeviceJobKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CountVerifiedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "DeviceJobKey",
                table: "Jobs");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_PrinterId",
                table: "Jobs",
                column: "PrinterId");
        }
    }
}
