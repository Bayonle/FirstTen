using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecognitionLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "recognition");

            migrationBuilder.CreateTable(
                name: "award_adjustments",
                schema: "recognition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_award_adjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consent_decisions",
                schema: "recognition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReporterKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Choice = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contribution_awards",
                schema: "recognition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReviewedIncidentLga = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    BadgeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AwardedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contribution_awards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_intents",
                schema: "recognition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactReference = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExactText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_intents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_award_adjustments_AwardId_OccurredAtUtc",
                schema: "recognition",
                table: "award_adjustments",
                columns: new[] { "AwardId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_consent_decisions_ReporterKey_DecidedAtUtc",
                schema: "recognition",
                table: "consent_decisions",
                columns: new[] { "ReporterKey", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_consent_decisions_SemanticKey",
                schema: "recognition",
                table: "consent_decisions",
                column: "SemanticKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contribution_awards_ContributionId",
                schema: "recognition",
                table: "contribution_awards",
                column: "ContributionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contribution_awards_ReviewedIncidentLga_AwardedAtUtc",
                schema: "recognition",
                table: "contribution_awards",
                columns: new[] { "ReviewedIncidentLga", "AwardedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_intents_AwardId",
                schema: "recognition",
                table: "notification_intents",
                column: "AwardId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "award_adjustments",
                schema: "recognition");

            migrationBuilder.DropTable(
                name: "consent_decisions",
                schema: "recognition");

            migrationBuilder.DropTable(
                name: "contribution_awards",
                schema: "recognition");

            migrationBuilder.DropTable(
                name: "notification_intents",
                schema: "recognition");
        }
    }
}
