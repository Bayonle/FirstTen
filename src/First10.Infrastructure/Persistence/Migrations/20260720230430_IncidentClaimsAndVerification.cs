using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentClaimsAndVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conflicts_IncidentId_Field_LeftReportId_RightReportId",
                schema: "incidents",
                table: "conflicts");

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                schema: "incidents",
                table: "source_reports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "LocationDescription",
                schema: "incidents",
                table: "source_reports",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SceneState",
                schema: "incidents",
                table: "source_reports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "VerifiedPilotIdentityKey",
                schema: "incidents",
                table: "source_reports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VictimState",
                schema: "incidents",
                table: "source_reports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                schema: "incidents",
                table: "review_alerts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "High");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAtUtc",
                schema: "incidents",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "incidents",
                table: "incidents",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeftClaimId",
                schema: "incidents",
                table: "conflicts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "RightClaimId",
                schema: "incidents",
                table: "conflicts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SelectedClaimId",
                schema: "incidents",
                table: "conflicts",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE incidents.conflicts
                SET "LeftClaimId" = "LeftReportId", "RightClaimId" = "RightReportId";
                """);

            migrationBuilder.CreateTable(
                name: "observations",
                schema: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    VictimState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SceneState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocationDescription = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_observations_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "incidents",
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_observations_source_reports_SourceReportId",
                        column: x => x.SourceReportId,
                        principalSchema: "incidents",
                        principalTable: "source_reports",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conflicts_IncidentId_Field_LeftClaimId_RightClaimId",
                schema: "incidents",
                table: "conflicts",
                columns: new[] { "IncidentId", "Field", "LeftClaimId", "RightClaimId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_observations_IncidentId_OccurredAtUtc",
                schema: "incidents",
                table: "observations",
                columns: new[] { "IncidentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_observations_SourceReportId",
                schema: "incidents",
                table: "observations",
                column: "SourceReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observations",
                schema: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_conflicts_IncidentId_Field_LeftClaimId_RightClaimId",
                schema: "incidents",
                table: "conflicts");

            migrationBuilder.DropColumn(
                name: "Direction",
                schema: "incidents",
                table: "source_reports");

            migrationBuilder.DropColumn(
                name: "LocationDescription",
                schema: "incidents",
                table: "source_reports");

            migrationBuilder.DropColumn(
                name: "SceneState",
                schema: "incidents",
                table: "source_reports");

            migrationBuilder.DropColumn(
                name: "VerifiedPilotIdentityKey",
                schema: "incidents",
                table: "source_reports");

            migrationBuilder.DropColumn(
                name: "VictimState",
                schema: "incidents",
                table: "source_reports");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "incidents",
                table: "review_alerts");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                schema: "incidents",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "incidents",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "LeftClaimId",
                schema: "incidents",
                table: "conflicts");

            migrationBuilder.DropColumn(
                name: "RightClaimId",
                schema: "incidents",
                table: "conflicts");

            migrationBuilder.DropColumn(
                name: "SelectedClaimId",
                schema: "incidents",
                table: "conflicts");

            migrationBuilder.CreateIndex(
                name: "IX_conflicts_IncidentId_Field_LeftReportId_RightReportId",
                schema: "incidents",
                table: "conflicts",
                columns: new[] { "IncidentId", "Field", "LeftReportId", "RightReportId" },
                unique: true);
        }
    }
}
