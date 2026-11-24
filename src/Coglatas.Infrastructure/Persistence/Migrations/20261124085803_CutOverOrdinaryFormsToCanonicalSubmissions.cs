using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CutOverOrdinaryFormsToCanonicalSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLegacyObservedSnapshot",
                table: "form_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasUninterpretedLegacyValue",
                table: "form_submission_values",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LegacyAnswerJson",
                table: "form_submission_values",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyAnswerText",
                table: "form_submission_values",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegacyQuestionId",
                table: "form_submission_values",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "VersionNo",
                table: "form_definitions",
                type: "bigint",
                nullable: false,
                defaultValue: 1L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "Purpose",
                table: "form_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Ordinary",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LegacyClosesAt",
                table: "form_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyFormType",
                table: "form_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LegacyIsAnonymous",
                table: "form_definitions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LegacyOpensAt",
                table: "form_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(OrdinaryFormsCutoverSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OrdinaryFormsCutoverSql.Down);

            migrationBuilder.DropColumn(
                name: "IsLegacyObservedSnapshot",
                table: "form_versions");

            migrationBuilder.DropColumn(
                name: "HasUninterpretedLegacyValue",
                table: "form_submission_values");

            migrationBuilder.DropColumn(
                name: "LegacyAnswerJson",
                table: "form_submission_values");

            migrationBuilder.DropColumn(
                name: "LegacyAnswerText",
                table: "form_submission_values");

            migrationBuilder.DropColumn(
                name: "LegacyQuestionId",
                table: "form_submission_values");

            migrationBuilder.DropColumn(
                name: "LegacyClosesAt",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "LegacyFormType",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "LegacyIsAnonymous",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "LegacyOpensAt",
                table: "form_definitions");

            migrationBuilder.AlterColumn<long>(
                name: "VersionNo",
                table: "form_definitions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 1L);

            migrationBuilder.AlterColumn<string>(
                name: "Purpose",
                table: "form_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: "Ordinary");
        }
    }
}
