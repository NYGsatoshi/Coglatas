namespace Coglatas.Infrastructure.Persistence.Migrations;

internal static class OrdinaryFormsCutoverSql
{
    internal const string Up = """
        LOCK TABLE internal_forms, form_questions, form_responses, form_answers IN ACCESS EXCLUSIVE MODE;

        -- The source is fenced for the whole migration transaction.
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM internal_forms AS form
                WHERE num_nonnulls(form."WorkspaceId", form."GroupId", form."ProjectId") <> 1
                    OR form."FormType" NOT IN ('Survey', 'Application', 'AttendanceSupplement', 'Preference', 'Checklist', 'Other')
                    OR form."Status" NOT IN ('Draft', 'Open', 'Closed', 'Archived')
            ) OR EXISTS (
                SELECT 1 FROM form_questions AS question
                JOIN internal_forms AS form ON form."Id" = question."FormId"
                WHERE question."TenantId" <> form."TenantId"
                    OR question."QuestionType" NOT IN ('ShortText', 'LongText', 'SingleChoice', 'MultipleChoice', 'Boolean', 'Date', 'Number')
            ) OR EXISTS (
                SELECT 1 FROM form_responses AS response
                JOIN internal_forms AS form ON form."Id" = response."FormId"
                WHERE response."TenantId" <> form."TenantId"
            ) OR EXISTS (
                SELECT 1 FROM form_answers AS answer
                JOIN form_responses AS response ON response."Id" = answer."FormResponseId"
                JOIN form_questions AS question ON question."Id" = answer."FormQuestionId"
                WHERE answer."TenantId" <> response."TenantId" OR answer."TenantId" <> question."TenantId"
                    OR response."FormId" <> question."FormId"
            ) THEN
                RAISE EXCEPTION 'Legacy Forms relationships require reconciliation before cutover' USING ERRCODE = '23514';
            END IF;
        END;
        $$;

        INSERT INTO form_definitions (
            "Id", "TenantId", "WorkspaceId", "GroupId", "ProjectId", "CreatedByUserId",
            "Title", "Description", "Status", "Purpose", "VersionNo",
            "CreatedAt", "UpdatedAt", "DeletedAt", "DeletedByUserId", "DeleteReason",
            "LegacyFormType", "LegacyOpensAt", "LegacyClosesAt", "LegacyIsAnonymous")
        SELECT "Id", "TenantId", "WorkspaceId", "GroupId", "ProjectId", "CreatedByUserId",
               "Title", "Description", "Status", 'Ordinary', 1,
               "CreatedAt", "UpdatedAt", "DeletedAt", "DeletedByUserId", "DeleteReason",
               "FormType", "OpensAt", "ClosesAt", "IsAnonymous"
        FROM internal_forms;

        -- RFC 9562 UUIDv8 with explicitly masked version and variant bits.
        -- Prefix the lowercase names with the migration namespace bytes.
        CREATE FUNCTION ordinary_form_stable_uuid(key_name text) RETURNS uuid
        LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $$
            SELECT encode(
                set_byte(set_byte(hash_bytes, 6, (get_byte(hash_bytes, 6) & 15) | 128),
                    8, (get_byte(hash_bytes, 8) & 63) | 128),
                'hex')::uuid
            FROM (SELECT substring(
                sha256(uuid_send('68e01436-1a0e-5dea-bb2e-cb52e18f268c'::uuid) || convert_to(key_name, 'UTF8')),
                1, 16) AS hash_bytes) AS digest;
        $$;

        CREATE FUNCTION ordinary_form_snapshot(form_id uuid, tenant_id uuid, version_number integer)
        RETURNS jsonb LANGUAGE sql STABLE AS $$
            SELECT COALESCE(jsonb_agg(
                jsonb_build_object(
                    'fieldKey', question."Id"::text,
                    'label', question."QuestionText",
                    'type', CASE question."QuestionType"
                        WHEN 'ShortText' THEN 'text' WHEN 'LongText' THEN 'text'
                        ELSE lower(left(question."QuestionType", 1)) || substring(question."QuestionType", 2)
                    END,
                    'required', question."IsRequired",
                    'options', COALESCE((
                        SELECT jsonb_agg(jsonb_build_object(
                            'optionKey', ordinary_form_stable_uuid(
                                'option/' || tenant_id || '/' || form_id || '/' || question."Id" || '/'
                                    || version_number || '/' || (choice.ordinal - 1)),
                            'label', choice.label) ORDER BY choice.ordinal)
                        FROM jsonb_array_elements_text(
                            CASE WHEN question."QuestionType" IN ('SingleChoice', 'MultipleChoice')
                                AND jsonb_typeof(question."OptionsJson") = 'array'
                                AND NOT jsonb_path_exists(question."OptionsJson", '$[*] ? (@.type() != "string")')
                                THEN question."OptionsJson" ELSE '[]'::jsonb END
                        ) WITH ORDINALITY AS choice(label, ordinal)
                    ), '[]'::jsonb)
                ) ORDER BY question."SortOrder", question."CreatedAt", question."Id"), '[]'::jsonb)
            FROM form_questions AS question
            WHERE question."FormId" = form_id AND question."TenantId" = tenant_id;
        $$;

        CREATE FUNCTION ordinary_form_version(form_id uuid, tenant_id uuid, is_legacy_snapshot boolean, published_at timestamptz)
        RETURNS uuid LANGUAGE plpgsql AS $$
        DECLARE
            snapshot jsonb;
            latest_version record;
            next_number integer;
            version_id uuid;
        BEGIN
            PERFORM 1 FROM form_definitions
            WHERE "Id" = form_id AND "TenantId" = tenant_id AND "Purpose" = 'Ordinary'
                AND "LegacyFormType" IS NOT NULL
            FOR UPDATE;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'Ordinary response lacks its owning form' USING ERRCODE = '23503';
            END IF;

            SELECT * INTO latest_version
            FROM form_versions
            WHERE "FormDefinitionId" = form_id AND "TenantId" = tenant_id
            ORDER BY "Number" DESC LIMIT 1;

            next_number := COALESCE(latest_version."Number", 1);
            snapshot := ordinary_form_snapshot(form_id, tenant_id, next_number);
            IF latest_version."Id" IS NOT NULL THEN
                IF latest_version."SnapshotJson"::jsonb = snapshot THEN
                    RETURN latest_version."Id";
                END IF;
                next_number := next_number + 1;
                snapshot := ordinary_form_snapshot(form_id, tenant_id, next_number);
            END IF;

            version_id := ordinary_form_stable_uuid('form-version/' || tenant_id || '/' || form_id || '/' || next_number);
            INSERT INTO form_versions (
                "Id", "TenantId", "FormDefinitionId", "Number", "SnapshotJson", "PublishedAtUtc",
                "CreatedAt", "IsLegacyObservedSnapshot")
            VALUES (version_id, tenant_id, form_id, next_number, snapshot::text, published_at, published_at, is_legacy_snapshot);

            INSERT INTO form_field_definitions (
                "Id", "TenantId", "FormVersionId", "FieldKey", "Label", "FieldType", "SortOrder",
                "IsRequired", "OptionsJson", "CreatedAt")
            SELECT ordinary_form_stable_uuid(
                       'field-version/' || tenant_id || '/' || form_id || '/' || (field.value->>'fieldKey') || '/' || next_number),
                   tenant_id, version_id, field.value->>'fieldKey', field.value->>'label',
                   upper(left(field.value->>'type', 1)) || substring(field.value->>'type', 2),
                   field.ordinal - 1, (field.value->>'required')::boolean, field.value->'options', published_at
            FROM jsonb_array_elements(snapshot) WITH ORDINALITY AS field(value, ordinal);

            RETURN version_id;
        END;
        $$;

        -- Capture definitions with no responses as well as forms with history.
        SELECT ordinary_form_version("Id", "TenantId", true, COALESCE("UpdatedAt", "CreatedAt"))
        FROM internal_forms
        ORDER BY "TenantId", "Id";

        CREATE FUNCTION ordinary_form_record_submission(
            response_id uuid, tenant_id uuid, form_id uuid, respondent_id uuid, submitted_at timestamptz,
            created_at timestamptz, updated_at timestamptz, is_legacy_snapshot boolean)
        RETURNS void LANGUAGE plpgsql AS $$
        DECLARE
            version_id uuid;
        BEGIN
            IF is_legacy_snapshot THEN
                SELECT "Id" INTO STRICT version_id
                FROM form_versions
                WHERE "FormDefinitionId" = form_id AND "TenantId" = tenant_id AND "Number" = 1;
            ELSE
                version_id := ordinary_form_version(form_id, tenant_id, false, submitted_at);
            END IF;

            INSERT INTO form_submissions (
                "Id", "TenantId", "FormDefinitionId", "FormVersionId", "RespondentUserId", "SubmittedAtUtc",
                "Status", "VersionNo", "FieldLockSnapshotJson", "CreatedAt", "UpdatedAt")
            VALUES (response_id, tenant_id, form_id, version_id, respondent_id, submitted_at, 'Submitted', 1, '{}', created_at, updated_at);
        END;
        $$;

        SELECT ordinary_form_record_submission(
            "Id", "TenantId", "FormId", "RespondentUserId", "SubmittedAt", "CreatedAt", "UpdatedAt", true)
        FROM form_responses
        ORDER BY "TenantId", "Id";

        CREATE FUNCTION ordinary_form_record_answer(
            answer_id uuid, tenant_id uuid, response_id uuid, question_id uuid, answer_text text, answer_json jsonb,
            created_at timestamptz, updated_at timestamptz)
        RETURNS void LANGUAGE plpgsql AS $$
        DECLARE
            field_record record;
            mapped jsonb;
        BEGIN
            SELECT field."Id", field."FieldType", field."OptionsJson" INTO STRICT field_record
            FROM form_submissions AS submission
            JOIN form_field_definitions AS field
              ON field."FormVersionId" = submission."FormVersionId" AND field."TenantId" = submission."TenantId"
            WHERE submission."Id" = response_id AND submission."TenantId" = tenant_id
                AND submission."SurveyTargetId" IS NULL AND field."FieldKey" = question_id::text;

            IF field_record."FieldType" = 'Text' THEN
                mapped := to_jsonb(answer_text);
            ELSIF field_record."FieldType" = 'Date' AND answer_text ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'
                AND pg_input_is_valid(answer_text, 'date') THEN
                mapped := to_jsonb(answer_text);
            ELSIF field_record."FieldType" = 'Boolean' AND answer_text IN ('true', 'false') THEN
                mapped := to_jsonb(answer_text::boolean);
            ELSIF field_record."FieldType" = 'Number' AND char_length(answer_text) <= 31 AND answer_text ~ '^-?[0-9]+(\.[0-9]+)?$' THEN
                IF abs(answer_text::numeric) <= 79228162514264337593543950335
                    AND scale(answer_text::numeric) <= 28 THEN
                    mapped := to_jsonb(answer_text::numeric);
                END IF;
            ELSIF field_record."FieldType" = 'SingleChoice' THEN
                SELECT CASE WHEN count(*) = 1 THEN to_jsonb(min(choice->>'optionKey')) END INTO mapped
                FROM jsonb_array_elements(field_record."OptionsJson") AS choice
                WHERE (choice->>'label') COLLATE "C" = answer_text COLLATE "C";
            ELSIF field_record."FieldType" = 'MultipleChoice' AND jsonb_typeof(answer_json) = 'array' THEN
                SELECT CASE WHEN bool_and(selected.match_count = 1)
                    THEN jsonb_agg(to_jsonb(selected.option_key) ORDER BY answer.ordinal) END INTO mapped
                FROM jsonb_array_elements(answer_json) WITH ORDINALITY AS answer(label, ordinal)
                CROSS JOIN LATERAL (
                    SELECT count(*) AS match_count, min(choice->>'optionKey') AS option_key
                    FROM jsonb_array_elements(field_record."OptionsJson") AS choice
                    WHERE jsonb_typeof(answer.label) = 'string'
                        AND (choice->>'label') COLLATE "C" = (answer.label#>>'{}') COLLATE "C"
                ) AS selected;
            END IF;

            INSERT INTO form_submission_values (
                "Id", "TenantId", "FormSubmissionId", "FormFieldDefinitionId", "ValueJson", "CreatedAt", "UpdatedAt",
                "LegacyQuestionId", "LegacyAnswerText", "LegacyAnswerJson", "HasUninterpretedLegacyValue")
            VALUES (
                answer_id, tenant_id, response_id, field_record."Id", COALESCE(mapped, 'null'::jsonb), created_at, updated_at,
                question_id, answer_text, answer_json, mapped IS NULL);
        END;
        $$;

        SELECT ordinary_form_record_answer(
            "Id", "TenantId", "FormResponseId", "FormQuestionId", "AnswerText", "AnswerJson", "CreatedAt", "UpdatedAt")
        FROM form_answers
        ORDER BY "TenantId", "Id";

        DO $$
        BEGIN
            IF (SELECT count(*) FROM internal_forms) <>
                    (SELECT count(*) FROM form_definitions WHERE "LegacyFormType" IS NOT NULL)
                OR EXISTS (
                    SELECT "Id" FROM form_responses EXCEPT SELECT "Id" FROM form_submissions WHERE "SurveyTargetId" IS NULL
                ) OR EXISTS (
                    SELECT "Id" FROM form_answers EXCEPT SELECT "Id" FROM form_submission_values WHERE "LegacyQuestionId" IS NOT NULL
                ) THEN
                RAISE EXCEPTION 'Ordinary Forms canonical reconciliation failed' USING ERRCODE = '23514';
            END IF;
        END;
        $$;

        ALTER TABLE form_questions DROP CONSTRAINT "FK_form_questions_internal_forms_FormId";
        ALTER TABLE form_questions ADD CONSTRAINT "FK_form_questions_form_definitions_FormId"
            FOREIGN KEY ("FormId", "TenantId") REFERENCES form_definitions ("Id", "TenantId") ON DELETE RESTRICT;

        CREATE FUNCTION synchronize_ordinary_question_authoring() RETURNS trigger
        LANGUAGE plpgsql AS $$
        DECLARE
            owning_form_id uuid;
            owning_tenant_id uuid;
        BEGIN
            IF TG_OP = 'INSERT' THEN
                owning_form_id := NEW."FormId";
                owning_tenant_id := NEW."TenantId";
            ELSE
                owning_form_id := OLD."FormId";
                owning_tenant_id := OLD."TenantId";
            END IF;

            PERFORM 1 FROM form_definitions
            WHERE "Id" = owning_form_id AND "TenantId" = owning_tenant_id
            FOR UPDATE;

            IF (TG_OP = 'DELETE' OR (TG_OP = 'UPDATE' AND OLD."QuestionType" <> NEW."QuestionType"))
                AND EXISTS (
                    SELECT 1 FROM form_submissions
                    WHERE "FormDefinitionId" = owning_form_id AND "TenantId" = owning_tenant_id
                        AND "SurveyTargetId" IS NULL)
            THEN
                RAISE EXCEPTION 'Answered ordinary question structure is fixed' USING ERRCODE = '23514';
            END IF;

            IF TG_OP = 'DELETE' THEN
                RETURN OLD;
            END IF;
            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER ordinary_question_authoring_fence BEFORE INSERT OR UPDATE OR DELETE ON form_questions
            FOR EACH ROW EXECUTE FUNCTION synchronize_ordinary_question_authoring();

        ALTER TABLE form_answers RENAME TO pre_aq_form_answers;
        ALTER TABLE form_responses RENAME TO pre_aq_form_responses;
        ALTER TABLE internal_forms RENAME TO pre_aq_internal_forms;

        -- The retained source is evidence of the fenced snapshot, with no dependencies
        -- back into active scope, account or question deletion.
        ALTER TABLE pre_aq_internal_forms
            DROP CONSTRAINT "FK_internal_forms_groups_GroupId",
            DROP CONSTRAINT "FK_internal_forms_projects_ProjectId",
            DROP CONSTRAINT "FK_internal_forms_users_CreatedByUserId",
            DROP CONSTRAINT "FK_internal_forms_workspaces_WorkspaceId";
        ALTER TABLE pre_aq_form_responses DROP CONSTRAINT "FK_form_responses_users_RespondentUserId";
        ALTER TABLE pre_aq_form_answers DROP CONSTRAINT "FK_form_answers_form_questions_FormQuestionId";

        CREATE VIEW internal_forms WITH (security_invoker = true) AS
        SELECT "Id", "WorkspaceId", "GroupId", "ProjectId", "CreatedByUserId", "Title", "Description",
            "LegacyFormType" AS "FormType", "Status", "LegacyOpensAt" AS "OpensAt", "LegacyClosesAt" AS "ClosesAt",
            "LegacyIsAnonymous" AS "IsAnonymous", "CreatedAt", "UpdatedAt", "DeletedAt", "TenantId",
            "DeleteReason", "DeletedByUserId"
        FROM form_definitions
        WHERE "Purpose" = 'Ordinary' AND "LegacyFormType" IS NOT NULL
        WITH LOCAL CHECK OPTION;

        CREATE VIEW form_responses WITH (security_invoker = true) AS
        SELECT "Id", "FormDefinitionId" AS "FormId", "RespondentUserId",
            "SubmittedAtUtc" AS "SubmittedAt", "CreatedAt", "UpdatedAt", "TenantId"
        FROM form_submissions AS submission
        WHERE submission."SurveyTargetId" IS NULL AND EXISTS (
            SELECT 1 FROM form_definitions AS form
            WHERE form."Id" = submission."FormDefinitionId" AND form."TenantId" = submission."TenantId"
                AND form."LegacyFormType" IS NOT NULL);

        CREATE FUNCTION ordinary_form_response_view_insert() RETURNS trigger
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

        CREATE VIEW form_answers WITH (security_invoker = true) AS
        SELECT "Id", "FormSubmissionId" AS "FormResponseId", "LegacyQuestionId" AS "FormQuestionId",
            "LegacyAnswerText" AS "AnswerText", "LegacyAnswerJson" AS "AnswerJson", "CreatedAt", "UpdatedAt", "TenantId"
        FROM form_submission_values
        WHERE "LegacyQuestionId" IS NOT NULL;

        CREATE FUNCTION ordinary_form_answer_view_insert() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            PERFORM ordinary_form_record_answer(
                NEW."Id", NEW."TenantId", NEW."FormResponseId", NEW."FormQuestionId",
                NEW."AnswerText", NEW."AnswerJson", NEW."CreatedAt", NEW."UpdatedAt");
            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER form_answers_canonical_insert INSTEAD OF INSERT ON form_answers
            FOR EACH ROW EXECUTE FUNCTION ordinary_form_answer_view_insert();

        CREATE FUNCTION reject_fenced_legacy_form_write() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Legacy Forms source is fenced after canonical cutover' USING ERRCODE = '23514';
        END;
        $$;

        CREATE TRIGGER pre_aq_forms_fenced BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON pre_aq_internal_forms
            FOR EACH STATEMENT EXECUTE FUNCTION reject_fenced_legacy_form_write();
        CREATE TRIGGER pre_aq_responses_fenced BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON pre_aq_form_responses
            FOR EACH STATEMENT EXECUTE FUNCTION reject_fenced_legacy_form_write();
        CREATE TRIGGER pre_aq_answers_fenced BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON pre_aq_form_answers
            FOR EACH STATEMENT EXECUTE FUNCTION reject_fenced_legacy_form_write();

        CREATE TRIGGER legacy_form_responses_read_only INSTEAD OF UPDATE OR DELETE ON form_responses
            FOR EACH ROW EXECUTE FUNCTION reject_fenced_legacy_form_write();
        CREATE TRIGGER legacy_form_answers_read_only INSTEAD OF UPDATE OR DELETE ON form_answers
            FOR EACH ROW EXECUTE FUNCTION reject_fenced_legacy_form_write();
        """;

    internal const string Down = """
        LOCK TABLE form_definitions, form_questions, form_submissions, form_submission_values,
            pre_aq_internal_forms, pre_aq_form_responses, pre_aq_form_answers IN ACCESS EXCLUSIVE MODE;

        -- The retained snapshot can regain authority only while its live compatibility
        -- projection is identical. After writes or erasure, use a lossless recovery.
        DO $$
        BEGIN
            IF EXISTS (
                (SELECT * FROM internal_forms EXCEPT ALL SELECT * FROM pre_aq_internal_forms)
                UNION ALL
                (SELECT * FROM pre_aq_internal_forms EXCEPT ALL SELECT * FROM internal_forms)
            ) OR EXISTS (
                (SELECT * FROM form_responses EXCEPT ALL SELECT * FROM pre_aq_form_responses)
                UNION ALL
                (SELECT * FROM pre_aq_form_responses EXCEPT ALL SELECT * FROM form_responses)
            ) OR EXISTS (
                (SELECT * FROM form_answers EXCEPT ALL SELECT * FROM pre_aq_form_answers)
                UNION ALL
                (SELECT * FROM pre_aq_form_answers EXCEPT ALL SELECT * FROM form_answers)
            ) OR EXISTS (
                SELECT 1 FROM form_versions WHERE "Id" IN (
                    SELECT "FormVersionId" FROM form_submissions WHERE "SurveyTargetId" IS NULL)
                AND "Number" <> 1
            ) THEN
                RAISE EXCEPTION 'Ordinary Forms changed after the retained cutover snapshot' USING ERRCODE = '23514';
            END IF;
        END;
        $$;

        DROP VIEW form_answers;
        DROP VIEW form_responses;
        DROP VIEW internal_forms;
        DROP FUNCTION ordinary_form_answer_view_insert();
        DROP FUNCTION ordinary_form_response_view_insert();
        DROP TRIGGER pre_aq_answers_fenced ON pre_aq_form_answers;
        DROP TRIGGER pre_aq_responses_fenced ON pre_aq_form_responses;
        DROP TRIGGER pre_aq_forms_fenced ON pre_aq_internal_forms;
        DROP FUNCTION reject_fenced_legacy_form_write();
        DROP FUNCTION ordinary_form_record_answer(uuid, uuid, uuid, uuid, text, jsonb, timestamptz, timestamptz);
        DROP FUNCTION ordinary_form_record_submission(uuid, uuid, uuid, uuid, timestamptz, timestamptz, timestamptz, boolean);
        DROP FUNCTION ordinary_form_version(uuid, uuid, boolean, timestamptz);
        DROP FUNCTION ordinary_form_snapshot(uuid, uuid, integer);
        DROP FUNCTION ordinary_form_stable_uuid(text);

        ALTER TABLE pre_aq_internal_forms RENAME TO internal_forms;
        ALTER TABLE pre_aq_form_responses RENAME TO form_responses;
        ALTER TABLE pre_aq_form_answers RENAME TO form_answers;

        ALTER TABLE internal_forms
            ADD CONSTRAINT "FK_internal_forms_groups_GroupId"
                FOREIGN KEY ("GroupId") REFERENCES groups ("Id") ON DELETE SET NULL,
            ADD CONSTRAINT "FK_internal_forms_projects_ProjectId"
                FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE SET NULL,
            ADD CONSTRAINT "FK_internal_forms_users_CreatedByUserId"
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT,
            ADD CONSTRAINT "FK_internal_forms_workspaces_WorkspaceId"
                FOREIGN KEY ("WorkspaceId") REFERENCES workspaces ("Id") ON DELETE SET NULL;
        ALTER TABLE form_responses ADD CONSTRAINT "FK_form_responses_users_RespondentUserId"
            FOREIGN KEY ("RespondentUserId") REFERENCES users ("Id") ON DELETE SET NULL;
        ALTER TABLE form_answers ADD CONSTRAINT "FK_form_answers_form_questions_FormQuestionId"
            FOREIGN KEY ("FormQuestionId") REFERENCES form_questions ("Id") ON DELETE RESTRICT;

        DROP TRIGGER ordinary_question_authoring_fence ON form_questions;
        DROP FUNCTION synchronize_ordinary_question_authoring();

        ALTER TABLE form_questions DROP CONSTRAINT "FK_form_questions_form_definitions_FormId";
        ALTER TABLE form_questions ADD CONSTRAINT "FK_form_questions_internal_forms_FormId"
            FOREIGN KEY ("FormId") REFERENCES internal_forms ("Id") ON DELETE RESTRICT;

        DELETE FROM form_submission_values USING form_answers AS retained
            WHERE form_submission_values."Id" = retained."Id";
        DELETE FROM form_submissions USING form_responses AS retained
            WHERE form_submissions."Id" = retained."Id";

        ALTER TABLE form_field_definitions DISABLE TRIGGER form_field_definitions_immutable;
        ALTER TABLE form_versions DISABLE TRIGGER form_versions_immutable;
        DELETE FROM form_field_definitions AS field USING form_versions AS pinned, internal_forms AS retained
            WHERE field."FormVersionId" = pinned."Id" AND pinned."FormDefinitionId" = retained."Id";
        DELETE FROM form_versions USING internal_forms AS retained
            WHERE form_versions."FormDefinitionId" = retained."Id";
        ALTER TABLE form_versions ENABLE TRIGGER form_versions_immutable;
        ALTER TABLE form_field_definitions ENABLE TRIGGER form_field_definitions_immutable;

        DELETE FROM form_definitions USING internal_forms AS retained
            WHERE form_definitions."Id" = retained."Id";
        """;
}
