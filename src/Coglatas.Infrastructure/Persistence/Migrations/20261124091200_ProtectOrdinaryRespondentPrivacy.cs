using Microsoft.EntityFrameworkCore.Migrations;

namespace Coglatas.Infrastructure.Persistence.Migrations;

public partial class ProtectOrdinaryRespondentPrivacy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Ordinary response writes lock the owning form before inserting a
        // submission. Take these two table locks in the same order before DDL.
        migrationBuilder.Sql("LOCK TABLE form_definitions, form_submissions IN ACCESS EXCLUSIVE MODE;");

        migrationBuilder.AddColumn<bool>(
            name: "LegacyMaskRespondent",
            table: "form_submissions",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("""
            -- Older Forms stored only the current form flag. Their submission-time
            -- anonymity is unknown, so imported owners stay linked and masked.
            UPDATE form_submissions AS submission
            SET "LegacyMaskRespondent" = TRUE
            FROM form_definitions AS form
            WHERE submission."TenantId" = form."TenantId"
                AND submission."FormDefinitionId" = form."Id"
                AND form."LegacyFormType" IS NOT NULL
                AND submission."SurveyTargetId" IS NULL;

            CREATE OR REPLACE VIEW form_responses WITH (security_invoker = true) AS
            SELECT submission."Id", submission."FormDefinitionId" AS "FormId", submission."RespondentUserId",
                submission."SubmittedAtUtc" AS "SubmittedAt", submission."CreatedAt", submission."UpdatedAt", submission."TenantId",
                submission."LegacyMaskRespondent" AS "MaskRespondent"
            FROM form_submissions AS submission
            WHERE submission."SurveyTargetId" IS NULL AND EXISTS (
                SELECT 1 FROM form_definitions AS form
                WHERE form."Id" = submission."FormDefinitionId" AND form."TenantId" = submission."TenantId"
                    AND form."LegacyFormType" IS NOT NULL);

            CREATE OR REPLACE FUNCTION ordinary_form_response_view_insert() RETURNS trigger
            LANGUAGE plpgsql AS $$
            DECLARE
                should_mask boolean;
            BEGIN
                -- The form's write fence is held by ordinary_form_record_submission.
                PERFORM ordinary_form_record_submission(
                    NEW."Id", NEW."TenantId", NEW."FormId", NEW."RespondentUserId", NEW."SubmittedAt",
                    NEW."CreatedAt", NEW."UpdatedAt", false);
                SELECT "LegacyIsAnonymous" IS DISTINCT FROM FALSE OR COALESCE(NEW."MaskRespondent", FALSE) INTO STRICT should_mask
                FROM form_definitions WHERE "Id" = NEW."FormId" AND "TenantId" = NEW."TenantId";
                IF should_mask THEN
                    UPDATE form_submissions SET "LegacyMaskRespondent" = TRUE
                    WHERE "Id" = NEW."Id" AND "TenantId" = NEW."TenantId";
                END IF;
                NEW."MaskRespondent" := should_mask;
                RETURN NEW;
            END;
            $$;

            CREATE FUNCTION protect_ordinary_response_mask() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD."LegacyMaskRespondent" AND NOT NEW."LegacyMaskRespondent" THEN
                    RAISE EXCEPTION 'The response anonymity mask is permanent' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER ordinary_response_mask_is_permanent BEFORE UPDATE OF "LegacyMaskRespondent" ON form_submissions
                FOR EACH ROW EXECUTE FUNCTION protect_ordinary_response_mask();

            CREATE FUNCTION prevent_ordinary_anonymity_relaxation() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD."LegacyIsAnonymous" IS TRUE AND NEW."LegacyIsAnonymous" IS DISTINCT FROM TRUE
                    AND EXISTS (
                        SELECT 1 FROM form_submissions
                        WHERE "FormDefinitionId" = OLD."Id" AND "TenantId" = OLD."TenantId" AND "SurveyTargetId" IS NULL)
                THEN
                    RAISE EXCEPTION 'An anonymous form with responses cannot change to identified responses' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER ordinary_anonymity_is_monotone BEFORE UPDATE OF "LegacyIsAnonymous" ON form_definitions
                FOR EACH ROW EXECUTE FUNCTION prevent_ordinary_anonymity_relaxation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE form_definitions, form_submissions IN ACCESS EXCLUSIVE MODE;

            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM form_submissions WHERE "LegacyMaskRespondent" AND "SurveyTargetId" IS NULL) THEN
                    RAISE EXCEPTION 'Reversing the anonymity migration would unprotect accepted responses' USING ERRCODE = '23514';
                END IF;
            END;
            $$;

            DROP TRIGGER ordinary_anonymity_is_monotone ON form_definitions;
            DROP FUNCTION prevent_ordinary_anonymity_relaxation();
            DROP TRIGGER ordinary_response_mask_is_permanent ON form_submissions;
            DROP FUNCTION protect_ordinary_response_mask();
            DROP VIEW form_responses;

            CREATE VIEW form_responses WITH (security_invoker = true) AS
            SELECT submission."Id", submission."FormDefinitionId" AS "FormId", submission."RespondentUserId",
                submission."SubmittedAtUtc" AS "SubmittedAt", submission."CreatedAt", submission."UpdatedAt", submission."TenantId"
            FROM form_submissions AS submission
            WHERE submission."SurveyTargetId" IS NULL AND EXISTS (
                SELECT 1 FROM form_definitions AS form
                WHERE form."Id" = submission."FormDefinitionId" AND form."TenantId" = submission."TenantId"
                    AND form."LegacyFormType" IS NOT NULL);

            CREATE OR REPLACE FUNCTION ordinary_form_response_view_insert() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM ordinary_form_record_submission(
                    NEW."Id", NEW."TenantId", NEW."FormId", NEW."RespondentUserId", NEW."SubmittedAt",
                    NEW."CreatedAt", NEW."UpdatedAt", false);
                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER form_responses_canonical_insert INSTEAD OF INSERT ON form_responses
                FOR EACH ROW EXECUTE FUNCTION ordinary_form_response_view_insert();
            CREATE TRIGGER legacy_form_responses_read_only INSTEAD OF UPDATE OR DELETE ON form_responses
                FOR EACH ROW EXECUTE FUNCTION reject_fenced_legacy_form_write();
            """);

        migrationBuilder.DropColumn(name: "LegacyMaskRespondent", table: "form_submissions");
    }
}
