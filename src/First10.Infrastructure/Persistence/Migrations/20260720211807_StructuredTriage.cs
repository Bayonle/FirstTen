using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StructuredTriage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "triage_cases",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadlineAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AuthoritativeVersion = table.Column<int>(type: "integer", nullable: false),
                    AuthoritativeAssessmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ManualReviewRaisedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triage_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triage_cases_guided_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "intake",
                        principalTable: "guided_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manual_triage_alerts",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TriageCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_triage_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_manual_triage_alerts_triage_cases_TriageCaseId",
                        column: x => x.TriageCaseId,
                        principalSchema: "intake",
                        principalTable: "triage_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "triage_assessments",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TriageCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CasualtyMinimum = table.Column<int>(type: "integer", nullable: true),
                    CasualtyMaximum = table.Column<int>(type: "integer", nullable: true),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocationPhrase = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Uncertainty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GuidanceCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceReferences = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ModelConfiguration = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsAuthoritative = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triage_assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triage_assessments_triage_cases_TriageCaseId",
                        column: x => x.TriageCaseId,
                        principalSchema: "intake",
                        principalTable: "triage_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_manual_triage_alerts_Status_Priority_CreatedAtUtc",
                schema: "intake",
                table: "manual_triage_alerts",
                columns: new[] { "Status", "Priority", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_manual_triage_alerts_TriageCaseId",
                schema: "intake",
                table: "manual_triage_alerts",
                column: "TriageCaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_triage_assessments_TriageCaseId_ReceivedAtUtc",
                schema: "intake",
                table: "triage_assessments",
                columns: new[] { "TriageCaseId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_triage_cases_SessionId",
                schema: "intake",
                table: "triage_cases",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_triage_cases_Status_DeadlineAtUtc",
                schema: "intake",
                table: "triage_cases",
                columns: new[] { "Status", "DeadlineAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manual_triage_alerts",
                schema: "intake");

            migrationBuilder.DropTable(
                name: "triage_assessments",
                schema: "intake");

            migrationBuilder.DropTable(
                name: "triage_cases",
                schema: "intake");
        }
    }
}
