using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntakeDeliveryRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                schema: "intake",
                table: "prompt_intents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "intake",
                table: "prompt_intents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE intake.prompt_intents AS prompt
                SET "Channel" = session."Channel"
                FROM intake.guided_sessions AS session
                WHERE prompt."SessionId" = session."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Channel",
                schema: "intake",
                table: "prompt_intents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                schema: "intake",
                table: "prompt_intents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAtUtc",
                schema: "intake",
                table: "prompt_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                schema: "intake",
                table: "prompt_intents",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusChangedAtUtc",
                schema: "intake",
                table: "prompt_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recovery_items",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContactReference = table.Column<Guid>(type: "uuid", nullable: true),
                    ReporterKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_items", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_prompt_intents_Channel_ContactReference_ProviderMessageId",
                schema: "intake",
                table: "prompt_intents",
                columns: new[] { "Channel", "ContactReference", "ProviderMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_items_Channel_ProviderMessageId_Reason",
                schema: "intake",
                table: "recovery_items",
                columns: new[] { "Channel", "ProviderMessageId", "Reason" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recovery_items",
                schema: "intake");

            migrationBuilder.DropIndex(
                name: "IX_prompt_intents_Channel_ContactReference_ProviderMessageId",
                schema: "intake",
                table: "prompt_intents");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                schema: "intake",
                table: "prompt_intents");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "intake",
                table: "prompt_intents");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                schema: "intake",
                table: "prompt_intents");

            migrationBuilder.DropColumn(
                name: "LastAttemptAtUtc",
                schema: "intake",
                table: "prompt_intents");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                schema: "intake",
                table: "prompt_intents");

            migrationBuilder.DropColumn(
                name: "StatusChangedAtUtc",
                schema: "intake",
                table: "prompt_intents");
        }
    }
}
