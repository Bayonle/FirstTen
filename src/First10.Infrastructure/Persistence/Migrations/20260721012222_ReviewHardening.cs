using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                schema: "guidance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuidanceIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Component = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_intents_GuidanceIntentId",
                        column: x => x.GuidanceIntentId,
                        principalSchema: "guidance",
                        principalTable: "intents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_source_reports_OccurredAtUtc_IncidentId",
                schema: "incidents",
                table: "source_reports",
                columns: new[] { "OccurredAtUtc", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_GuidanceIntentId_Component_AttemptNumber",
                schema: "guidance",
                table: "delivery_attempts",
                columns: new[] { "GuidanceIntentId", "Component", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_ProviderMessageId",
                schema: "guidance",
                table: "delivery_attempts",
                column: "ProviderMessageId",
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts",
                schema: "guidance");

            migrationBuilder.DropIndex(
                name: "IX_source_reports_OccurredAtUtc_IncidentId",
                schema: "incidents",
                table: "source_reports");
        }
    }
}
