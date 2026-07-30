using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DispatchAndGuidance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dispatch");

            migrationBuilder.EnsureSchema(
                name: "guidance");

            migrationBuilder.CreateTable(
                name: "incident_dispatches",
                schema: "dispatch",
                columns: table => new
                {
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReopenReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_dispatches", x => x.IncidentId);
                    table.ForeignKey(
                        name: "FK_incident_dispatches_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "incidents",
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "intents",
                schema: "guidance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriageCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactReference = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TemplateSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    PolicyVersion = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    ExactText = table.Column<string>(type: "text", nullable: false),
                    TextSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VoiceAssetKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    VoiceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadlineAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "template_sets",
                schema: "guidance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SeverityBand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    EligibilityContext = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsConservativeDefault = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EnabledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_sets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transitions",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DispatcherId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResultingVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transitions_incident_dispatches_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "dispatch",
                        principalTable: "incident_dispatches",
                        principalColumn: "IncidentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_assets",
                schema: "guidance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExactText = table.Column<string>(type: "text", nullable: false),
                    TextSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VoiceAssetKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    VoiceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeechModel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SpeechVoice = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SpeechSettings = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_assets_template_sets_TemplateSetId",
                        column: x => x.TemplateSetId,
                        principalSchema: "guidance",
                        principalTable: "template_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_intents_SemanticKey",
                schema: "guidance",
                table: "intents",
                column: "SemanticKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_intents_Status_DeadlineAtUtc",
                schema: "guidance",
                table: "intents",
                columns: new[] { "Status", "DeadlineAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_template_assets_TemplateSetId_Language",
                schema: "guidance",
                table: "template_assets",
                columns: new[] { "TemplateSetId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_sets_Purpose_Trigger_Category_SeverityBand_Enabled~",
                schema: "guidance",
                table: "template_sets",
                columns: new[] { "Purpose", "Trigger", "Category", "SeverityBand", "EnabledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_template_sets_TemplateKey_PolicyVersion",
                schema: "guidance",
                table: "template_sets",
                columns: new[] { "TemplateKey", "PolicyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transitions_IncidentId_OccurredAtUtc",
                schema: "dispatch",
                table: "transitions",
                columns: new[] { "IncidentId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "intents",
                schema: "guidance");

            migrationBuilder.DropTable(
                name: "template_assets",
                schema: "guidance");

            migrationBuilder.DropTable(
                name: "transitions",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "template_sets",
                schema: "guidance");

            migrationBuilder.DropTable(
                name: "incident_dispatches",
                schema: "dispatch");
        }
    }
}
