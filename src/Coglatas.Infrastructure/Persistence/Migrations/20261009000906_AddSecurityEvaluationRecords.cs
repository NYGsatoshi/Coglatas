using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityEvaluationRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_projects_Id_TenantId",
                table: "projects",
                columns: new[] { "Id", "TenantId" });

            migrationBuilder.CreateTable(
                name: "security_evaluation_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    InputDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BindingDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    IdentityJson = table.Column<string>(type: "jsonb", nullable: false),
                    EnforcementMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TerminalAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_evaluation_runs", x => x.Id);
                    table.UniqueConstraint("AK_security_evaluation_runs_Id_TenantId_ProjectId", x => new { x.Id, x.TenantId, x.ProjectId });
                    table.CheckConstraint("CK_security_evaluation_runs_context", "\"ContextKind\" IN ('coglatas.committed', 'coglatas.candidate', 'coglatas.scenario')");
                    table.CheckConstraint("CK_security_evaluation_runs_digest", "\"InputDigest\" ~ '^[0-9a-f]{64}$' AND \"BindingDigest\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_security_evaluation_runs_identity", "jsonb_typeof(\"IdentityJson\") = 'object'");
                    table.CheckConstraint("CK_security_evaluation_runs_lifecycle", "(\"Status\" = 'Pending' AND \"Outcome\" IS NULL AND \"TerminalAtUtc\" IS NULL AND \"ReasonCode\" = 'NotExecuted') OR\n(\"Status\" IN ('Completed', 'Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND \"TerminalAtUtc\" IS NOT NULL AND \"TerminalAtUtc\" >= \"CreatedAtUtc\"\n AND ((\"Status\" = 'Completed' AND \"Outcome\" IS NOT NULL AND \"Outcome\" IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR\n      (\"Status\" <> 'Completed' AND \"Outcome\" IS NULL)))");
                    table.CheckConstraint("CK_security_evaluation_runs_mode", "\"EnforcementMode\" IN ('Disabled', 'Shadow')");
                    table.CheckConstraint("CK_security_evaluation_runs_reason", "(\"Status\" IN ('Pending', 'NotExecuted') AND \"ReasonCode\" IN ('NotExecuted', 'RuleNotExecuted')) OR\n(\"Status\" = 'Failed' AND \"ReasonCode\" IN ('EvaluationFailed', 'RuleExecutionFailed')) OR\n(\"Status\" = 'Cancelled' AND \"ReasonCode\" = 'EvaluationCancelled') OR\n(\"Status\" = 'TimedOut' AND \"ReasonCode\" = 'EvaluationTimedOut') OR\n(\"Status\" = 'Completed' AND (\n    (\"Outcome\" = 'Allow' AND \"ReasonCode\" IN ('BindingsVerified', 'RevisionBindingVerified', 'PolicyBindingVerified', 'CompilerProvenanceVerified')) OR\n    (\"Outcome\" = 'Deny' AND \"ReasonCode\" = 'PolicyViolation') OR\n    (\"Outcome\" = 'Unknown' AND \"ReasonCode\" IN ('MissingEvidence', 'RevisionEvidenceMissing', 'PolicyEvidenceMissing', 'CompilerEvidenceMissing')) OR\n    (\"Outcome\" = 'Quarantine' AND \"ReasonCode\" IN ('BindingMismatch', 'RevisionBindingMismatch', 'PolicyBindingMismatch', 'CompilerProvenanceMismatch'))))");
                    table.CheckConstraint("CK_security_evaluation_runs_schema", "\"SchemaVersion\" = 1");
                    table.ForeignKey(
                        name: "FK_security_evaluation_runs_projects_ProjectId_TenantId",
                        columns: x => new { x.ProjectId, x.TenantId },
                        principalTable: "projects",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "security_evaluation_rule_results",
                columns: table => new
                {
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_evaluation_rule_results", x => new { x.EvaluationId, x.Sequence });
                    table.CheckConstraint("CK_security_evaluation_rule_results_execution", "(\"Status\" = 'Completed' AND \"Outcome\" IS NOT NULL AND \"Outcome\" IN ('Allow', 'Deny', 'Unknown', 'Quarantine')) OR\n(\"Status\" IN ('Failed', 'Cancelled', 'TimedOut', 'NotExecuted') AND \"Outcome\" IS NULL)");
                    table.CheckConstraint("CK_security_evaluation_rule_results_id", "\"RuleId\" ~ '^[A-Za-z0-9_./:+-]{1,128}$'");
                    table.CheckConstraint("CK_security_evaluation_rule_results_reason", "(\"Status\" IN ('Pending', 'NotExecuted') AND \"ReasonCode\" IN ('NotExecuted', 'RuleNotExecuted')) OR\n(\"Status\" = 'Failed' AND \"ReasonCode\" IN ('EvaluationFailed', 'RuleExecutionFailed')) OR\n(\"Status\" = 'Cancelled' AND \"ReasonCode\" = 'EvaluationCancelled') OR\n(\"Status\" = 'TimedOut' AND \"ReasonCode\" = 'EvaluationTimedOut') OR\n(\"Status\" = 'Completed' AND (\n    (\"Outcome\" = 'Allow' AND \"ReasonCode\" IN ('BindingsVerified', 'RevisionBindingVerified', 'PolicyBindingVerified', 'CompilerProvenanceVerified')) OR\n    (\"Outcome\" = 'Deny' AND \"ReasonCode\" = 'PolicyViolation') OR\n    (\"Outcome\" = 'Unknown' AND \"ReasonCode\" IN ('MissingEvidence', 'RevisionEvidenceMissing', 'PolicyEvidenceMissing', 'CompilerEvidenceMissing')) OR\n    (\"Outcome\" = 'Quarantine' AND \"ReasonCode\" IN ('BindingMismatch', 'RevisionBindingMismatch', 'PolicyBindingMismatch', 'CompilerProvenanceMismatch'))))");
                    table.CheckConstraint("CK_security_evaluation_rule_results_schema", "\"SchemaVersion\" = 1");
                    table.CheckConstraint("CK_security_evaluation_rule_results_sequence", "\"Sequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_security_evaluation_rule_results_security_evaluation_runs_E~",
                        columns: x => new { x.EvaluationId, x.TenantId, x.ProjectId },
                        principalTable: "security_evaluation_runs",
                        principalColumns: new[] { "Id", "TenantId", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_security_evaluation_rule_results_EvaluationId_RuleId",
                table: "security_evaluation_rule_results",
                columns: new[] { "EvaluationId", "RuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_security_evaluation_rule_results_EvaluationId_TenantId_Proj~",
                table: "security_evaluation_rule_results",
                columns: new[] { "EvaluationId", "TenantId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_security_evaluation_rule_results_TenantId",
                table: "security_evaluation_rule_results",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_security_evaluation_runs_ProjectId_TenantId",
                table: "security_evaluation_runs",
                columns: new[] { "ProjectId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_security_evaluation_runs_TenantId",
                table: "security_evaluation_runs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_security_evaluation_runs_TenantId_ProjectId_BranchId_Revisi~",
                table: "security_evaluation_runs",
                columns: new[] { "TenantId", "ProjectId", "BranchId", "RevisionId", "CandidateRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_security_evaluation_runs_TenantId_ProjectId_CreatedAtUtc",
                table: "security_evaluation_runs",
                columns: new[] { "TenantId", "ProjectId", "CreatedAtUtc" });

            migrationBuilder.Sql("""
                CREATE FUNCTION security_evaluation_metadata_shape(value jsonb, keys text[]) RETURNS boolean
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT jsonb_typeof(value) = 'object' AND value ?& keys
                        AND (SELECT count(*) FROM jsonb_object_keys(value)) = cardinality(keys);
                $$;

                CREATE FUNCTION security_evaluation_run_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    item jsonb;
                    name text;
                    rule_count integer;
                    priority integer;
                    expected_outcome text;
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'Pending' OR NOT security_evaluation_metadata_shape(NEW."IdentityJson",
                            ARRAY['SubjectUserId','OperationId','Resource','ClaimedSource','ExpectedSource',
                                  'ClaimedPolicy','ExpectedPolicy','ClaimedCompiler','ExpectedCompiler']) THEN
                            RAISE EXCEPTION 'Security evaluation must begin Pending with safe metadata';
                        END IF;
                        FOREACH name IN ARRAY ARRAY['Resource','ClaimedSource','ExpectedSource'] LOOP
                            item := NEW."IdentityJson" -> name;
                            IF (name = 'Resource' OR item <> 'null'::jsonb) AND NOT security_evaluation_metadata_shape(item,
                                ARRAY['ContextKind','BaseContextKind','TenantId','ProjectId','BranchId','RevisionId',
                                      'ProposalId','CandidateRevisionId','BaseBranchId','BaseRevisionId',
                                      'CapturedHeadBranchId','CapturedHeadRevisionId','ScenarioId','ScenarioRevisionId',
                                      'OverlayDigest','ContextDigest','InputDigest']) THEN
                                RAISE EXCEPTION 'Security Source metadata shape is invalid';
                            END IF;
                        END LOOP;
                        FOREACH name IN ARRAY ARRAY['ClaimedPolicy','ExpectedPolicy'] LOOP
                            item := NEW."IdentityJson" -> name;
                            IF item <> 'null'::jsonb AND NOT security_evaluation_metadata_shape(item,
                                ARRAY['PolicySetId','Version','ContentDigest','SchemaVersion']) THEN
                                RAISE EXCEPTION 'Security policy metadata shape is invalid';
                            END IF;
                        END LOOP;
                        FOREACH name IN ARRAY ARRAY['ClaimedCompiler','ExpectedCompiler'] LOOP
                            item := NEW."IdentityJson" -> name;
                            IF item <> 'null'::jsonb AND NOT security_evaluation_metadata_shape(item,
                                ARRAY['Version','BuildIdentity','GitCommitSha']) THEN
                                RAISE EXCEPTION 'Security compiler metadata shape is invalid';
                            END IF;
                        END LOOP;
                        item := NEW."IdentityJson" -> 'Resource';
                        IF (item ->> 'TenantId')::uuid IS DISTINCT FROM NEW."TenantId"
                            OR (item ->> 'ProjectId')::uuid IS DISTINCT FROM NEW."ProjectId"
                            OR (item ->> 'BranchId')::uuid IS DISTINCT FROM NEW."BranchId"
                            OR item ->> 'ContextKind' IS DISTINCT FROM NEW."ContextKind"
                            OR (item ->> 'RevisionId')::uuid IS DISTINCT FROM NEW."RevisionId"
                            OR (item ->> 'CandidateRevisionId')::uuid IS DISTINCT FROM NEW."CandidateRevisionId"
                            OR item ->> 'InputDigest' IS DISTINCT FROM NEW."InputDigest" THEN
                            RAISE EXCEPTION 'Security evaluation metadata scope is invalid';
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF OLD."Status" <> 'Pending' OR NEW."Status" = 'Pending' THEN
                        RAISE EXCEPTION 'Security evaluation can terminalize only once';
                    END IF;
                    IF (to_jsonb(NEW) - ARRAY['Status','Outcome','ReasonCode','TerminalAtUtc']) IS DISTINCT FROM
                       (to_jsonb(OLD) - ARRAY['Status','Outcome','ReasonCode','TerminalAtUtc']) THEN
                        RAISE EXCEPTION 'Security evaluation binding is immutable';
                    END IF;
                    SELECT count(*), max(CASE "Outcome" WHEN 'Quarantine' THEN 4 WHEN 'Deny' THEN 3
                        WHEN 'Unknown' THEN 2 WHEN 'Allow' THEN 1 END)
                        INTO rule_count, priority FROM security_evaluation_rule_results WHERE "EvaluationId" = NEW."Id";
                    IF rule_count > 0 AND (
                        (SELECT min("Sequence") FROM security_evaluation_rule_results WHERE "EvaluationId" = NEW."Id") <> 0 OR
                        (SELECT max("Sequence") FROM security_evaluation_rule_results WHERE "EvaluationId" = NEW."Id") <> rule_count - 1 OR
                        EXISTS (SELECT 1 FROM security_evaluation_rule_results previous
                            JOIN security_evaluation_rule_results following ON previous."EvaluationId" = following."EvaluationId"
                              AND previous."Sequence" + 1 = following."Sequence"
                            WHERE previous."EvaluationId" = NEW."Id" AND previous."RuleId" COLLATE "C" >= following."RuleId" COLLATE "C")) THEN
                        RAISE EXCEPTION 'Security rule sequence must be contiguous and ordinal';
                    END IF;
                    IF NEW."Status" = 'Completed' THEN
                        expected_outcome := CASE priority WHEN 4 THEN 'Quarantine' WHEN 3 THEN 'Deny'
                            WHEN 2 THEN 'Unknown' WHEN 1 THEN 'Allow' END;
                        IF rule_count = 0 OR NEW."Outcome" IS DISTINCT FROM expected_outcome OR
                           EXISTS (SELECT 1 FROM security_evaluation_rule_results
                               WHERE "EvaluationId" = NEW."Id" AND "Status" <> 'Completed') THEN
                            RAISE EXCEPTION 'Security final outcome must match completed rule coverage';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER security_evaluation_run_guard_trigger BEFORE INSERT OR UPDATE
                    ON security_evaluation_runs FOR EACH ROW EXECUTE FUNCTION security_evaluation_run_guard();

                CREATE FUNCTION security_evaluation_rule_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE parent_status text;
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'Security rule results are immutable';
                    ELSIF TG_OP = 'DELETE' THEN
                        -- Parent deletion is the future authorized retention adapter boundary.
                        IF EXISTS (SELECT 1 FROM security_evaluation_runs WHERE "Id" = OLD."EvaluationId") THEN
                            RAISE EXCEPTION 'Security rule results cannot be removed in place';
                        END IF;
                        RETURN OLD;
                    END IF;
                    SELECT "Status" INTO parent_status FROM security_evaluation_runs
                        WHERE "Id" = NEW."EvaluationId" AND "TenantId" = NEW."TenantId" AND "ProjectId" = NEW."ProjectId"
                        FOR UPDATE;
                    IF parent_status IS DISTINCT FROM 'Pending' THEN
                        RAISE EXCEPTION 'Security rule results require their Pending scoped run';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER security_evaluation_rule_guard_trigger BEFORE INSERT OR UPDATE OR DELETE
                    ON security_evaluation_rule_results FOR EACH ROW EXECUTE FUNCTION security_evaluation_rule_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER security_evaluation_rule_guard_trigger ON security_evaluation_rule_results;
                DROP TRIGGER security_evaluation_run_guard_trigger ON security_evaluation_runs;
                DROP FUNCTION security_evaluation_rule_guard();
                DROP FUNCTION security_evaluation_run_guard();
                DROP FUNCTION security_evaluation_metadata_shape(jsonb, text[]);
                """);
            migrationBuilder.DropTable(
                name: "security_evaluation_rule_results");

            migrationBuilder.DropTable(
                name: "security_evaluation_runs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_projects_Id_TenantId",
                table: "projects");
        }
    }
}
