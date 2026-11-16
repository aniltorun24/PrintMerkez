using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintMerkez.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateReportedDocumentPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReportedDocumentPages",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);
            // Earlier pilot mapped event 307 document pages to copy-inclusive impressions.
            // Preserve the observed field while withdrawing the unverified accounting value.
            migrationBuilder.Sql("""
                INSERT INTO AuditEntries (OccurredAtUtc, Actor, Entity, EntityKey, Action)
                SELECT strftime('%Y-%m-%d %H:%M:%f','now'), 'Veri yükseltme',
                       'MeasurementCorrection', 'win307', 'Windows belge sayfası toplam çıktıdan ayrıldı'
                WHERE EXISTS (SELECT 1 FROM Jobs WHERE Source=1 AND EventId LIKE 'win307-%' AND Copies IS NULL);
                UPDATE Jobs SET ReportedDocumentPages=Impressions, Impressions=NULL,
                    CostKurus=NULL, AppliedUnitKurus=NULL, AppliedTariffId=NULL, CostState=2
                WHERE Source=1 AND EventId LIKE 'win307-%' AND Copies IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportedDocumentPages",
                table: "Jobs");
        }
    }
}
