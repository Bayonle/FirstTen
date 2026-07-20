using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentConsolidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "Longitude",
                schema: "incidents",
                table: "incident_timeline_events",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AlterColumn<double>(
                name: "Latitude",
                schema: "incidents",
                table: "incident_timeline_events",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AddColumn<string>(
                name: "PayloadJson",
                schema: "incidents",
                table: "incident_timeline_events",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceivedAtUtc",
                schema: "incidents",
                table: "incident_timeline_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE incidents.incident_timeline_events
                SET "ReceivedAtUtc" = "OccurredAtUtc"
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ReceivedAtUtc",
                schema: "incidents",
                table: "incident_timeline_events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "incidents",
                schema: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewDueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationStatus = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "candidate_links",
                schema: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstIncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondIncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_candidate_links_incidents_FirstIncidentId",
                        column: x => x.FirstIncidentId,
                        principalSchema: "incidents",
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_candidate_links_incidents_SecondIncidentId",
                        column: x => x.SecondIncidentId,
                        principalSchema: "incidents",
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conflicts",
                schema: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Field = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    LeftReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    RightReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeftValue = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RightValue = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SelectedReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conflicts_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "incidents",
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_reports",
                schema: "incidents",
                columns: table => new
                {
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterIndependenceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    LocationConfidence = table.Column<double>(type: "double precision", nullable: true),
                    IncidentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CasualtyMinimum = table.Column<int>(type: "integer", nullable: true),
                    CasualtyMaximum = table.Column<int>(type: "integer", nullable: true),
                    EvidenceReferences = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_reports", x => x.ReportId);
                    table.ForeignKey(
                        name: "FK_source_reports_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "incidents",
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incident_timeline_events_IncidentId_ReceivedAtUtc",
                schema: "incidents",
                table: "incident_timeline_events",
                columns: new[] { "IncidentId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_candidate_links_FirstIncidentId_SecondIncidentId_Reason",
                schema: "incidents",
                table: "candidate_links",
                columns: new[] { "FirstIncidentId", "SecondIncidentId", "Reason" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_candidate_links_SecondIncidentId",
                schema: "incidents",
                table: "candidate_links",
                column: "SecondIncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_conflicts_IncidentId_Field_LeftReportId_RightReportId",
                schema: "incidents",
                table: "conflicts",
                columns: new[] { "IncidentId", "Field", "LeftReportId", "RightReportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incidents_VerificationStatus_ReviewDueAtUtc",
                schema: "incidents",
                table: "incidents",
                columns: new[] { "VerificationStatus", "ReviewDueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_source_reports_IncidentId_OccurredAtUtc",
                schema: "incidents",
                table: "source_reports",
                columns: new[] { "IncidentId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_links",
                schema: "incidents");

            migrationBuilder.DropTable(
                name: "conflicts",
                schema: "incidents");

            migrationBuilder.DropTable(
                name: "source_reports",
                schema: "incidents");

            migrationBuilder.DropTable(
                name: "incidents",
                schema: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_incident_timeline_events_IncidentId_ReceivedAtUtc",
                schema: "incidents",
                table: "incident_timeline_events");

            migrationBuilder.DropColumn(
                name: "PayloadJson",
                schema: "incidents",
                table: "incident_timeline_events");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUtc",
                schema: "incidents",
                table: "incident_timeline_events");

            migrationBuilder.AlterColumn<double>(
                name: "Longitude",
                schema: "incidents",
                table: "incident_timeline_events",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Latitude",
                schema: "incidents",
                table: "incident_timeline_events",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);
        }
    }
}
