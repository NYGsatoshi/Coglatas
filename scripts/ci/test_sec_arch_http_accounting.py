"""Deliberate-invalid controls for explicit HTTP execution accounting."""

import copy
from datetime import datetime, timezone
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import sec_arch_http_accounting as http
from sec_arch_http_theory_cases import INVITE_DENIAL, INVITE_PROJECTION, QUERY_CASES
from sec_arch_assembly_binding import assembly_path, loaded_assembly_path, SIX_ASSEMBLY_SCOPE

NOW = datetime(2026, 10, 10, 1, tzinfo=timezone.utc)
Q = "{" + http.NS["t"] + "}"


def execution(outcome="Passed", method=http.GANTT):
    root = ET.Element(Q + "TestRun")
    ET.SubElement(root, Q + "Times", start="2026-10-10T00:00:00Z", finish="2026-10-10T00:01:00Z")
    definition = ET.SubElement(ET.SubElement(root, Q + "TestDefinitions"), Q + "UnitTest", id="1", name=method)
    ET.SubElement(definition, Q + "Execution", id="1")
    group, member = method.rsplit(".", 1)
    ET.SubElement(definition, Q + "TestMethod", className=group, name=member)
    ET.SubElement(ET.SubElement(root, Q + "Results"), Q + "UnitTestResult", testId="1", executionId="1", testName=method,
                  outcome=outcome, startTime="2026-10-10T00:00:01Z", endTime="2026-10-10T00:00:59Z")
    ET.SubElement(ET.SubElement(root, Q + "ResultSummary"), Q + "Counters", total="1",
                  passed="1" if outcome == "Passed" else "0", failed="1" if outcome == "Failed" else "0",
                  executed="0" if outcome == "NotExecuted" else "1")
    return ET.tostring(root)


class HttpAccountingTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        source = http.METHOD_SOURCES[http.GANTT]
        (self.root / source).parent.mkdir(parents=True)
        (self.root / source).write_bytes(("public async Task " + http.GANTT.rsplit(".", 1)[1] + "() { }").encode())
        assemblies = {}
        for name in http.ASSEMBLIES:
            path = assembly_path(self.root, name)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(name.encode())
            copied = loaded_assembly_path(self.root, name)
            copied.parent.mkdir(parents=True, exist_ok=True)
            copied.write_bytes(name.encode())
            assemblies[name] = http.digest(path.read_bytes())
        self.inventory = {"schemaVersion": 1, "catalogScope": "ACTUAL_COMPOSED_TEST_HOST", "endpointCount": 1,
                          "webAssemblyDigest": assemblies["Coglatas.Web"],
                          "endpoints": [{"surfaceId": "synthetic", "method": "GET", "normalizedPath": "/api/projects/{projectId}/gantt",
                                         "authorizationRequired": False, "kind": "CONTROLLER"}]}
        def observation(control, status, code=None):
            return {"path": "/api/projects/{projectId}/gantt", "method": "GET", "control": control,
                    "observedStatus": status, "expectedStatus": status, "errorCode": code,
                    "observedAtUtc": "2026-10-10T00:00:30Z"}
        self.record = {"schemaVersion": 2, "assemblyBindingScope": SIX_ASSEMBLY_SCOPE,
                       "verifierMethod": http.GANTT, "sourcePath": source,
                       "environment": http.POSTGRES_COMPOSITION,
                       "sourceDigest": http.digest((self.root / source).read_bytes()), "assemblyDigests": assemblies,
                       "ownerApproval": None, "observations": [observation("AUTHORIZED_SAME_SCOPE", 200),
                           observation("ANONYMOUS", 401, "GANTT_AUTHENTICATION_REQUIRED"),
                           observation("CROSS_TENANT", 404, "GANTT_PROJECT_NOT_FOUND")]}
        self.trx = execution()
        self.receipt = {"schemaVersion": 2, "verifierId": "SEC-ARCH-EXECUTION-COVERAGE", "verifierVersion": "2",
                        "assemblyBindingScope": SIX_ASSEMBLY_SCOPE,
                        "candidateSha": "a" * 40, "environmentFingerprint": "b" * 64, "runId": "17", "runAttempt": "2",
                        "buildStampMatchesCandidate": True, "executionDigest": http.digest(self.trx), "assemblyDigests": assemblies}
        self.identity = ("a" * 40, "b" * 64, "17", "2")

    def account(self, record=None, trx=None, receipt=None, identity=None):
        return http.account(self.root, self.inventory, [record or self.record], trx or self.trx, NOW, receipt, identity)

    def extra_fixture(self, method):
        source = http.METHOD_SOURCES[method]
        (self.root / source).parent.mkdir(parents=True, exist_ok=True)
        (self.root / source).write_bytes(("public async Task " + method.rsplit(".", 1)[1] + "() { }").encode())
        record = copy.deepcopy(self.record)
        record.update(verifierMethod=method, sourcePath=source, sourceDigest=http.digest((self.root / source).read_bytes()),
                      environment=http.METHOD_ENVIRONMENTS[method], observations=[])
        self.inventory["endpoints"] = []
        for (verb, path), controls in http.EXTRA_RULES[method].items():
            self.inventory["endpoints"].append({"surfaceId": verb + path, "method": verb, "normalizedPath": path,
                                               "authorizationRequired": True, "kind": "CONTROLLER"})
            for control, (status, code, assertion) in controls.items():
                record["observations"].append({"method": verb, "path": path, "control": control,
                    "observedStatus": status, "expectedStatus": status, "errorCode": code, "responseAssertion": assertion,
                    "observedAtUtc": "2026-10-10T00:00:29Z" if method in http.PRIOR_OPERATION_POSITIVE_METHODS and
                        control == "AUTHORIZED_SAME_SCOPE" else "2026-10-10T00:00:30Z"})
        self.inventory["endpointCount"] = len(self.inventory["endpoints"])
        return record, execution(method=method)

    def finite_fixture(self, method=INVITE_DENIAL):
        source = http.METHOD_SOURCES[method]
        path = self.root / source
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(('[Theory]\n' + ''.join('[InlineData("' + query + '")]\n' for query in QUERY_CASES.values()) +
                          'public async Task ' + method.rsplit('.', 1)[1] + '(string query) { }').encode())
        self.inventory['endpoints'] = [{'surfaceId': 'invites', 'method': 'GET', 'normalizedPath': '/api/admin/invites',
                                        'authorizationRequired': True, 'kind': 'CONTROLLER'}]
        self.inventory['endpointCount'] = 1
        root = ET.Element(Q + 'TestRun')
        ET.SubElement(root, Q + 'Times', start='2026-10-10T00:00:00Z', finish='2026-10-10T00:01:00Z')
        definitions, results = ET.SubElement(root, Q + 'TestDefinitions'), ET.SubElement(root, Q + 'Results')
        records = []
        for index, (case, name) in enumerate(http.THEORY_CASES[method].items()):
            identity = str(index)
            definition = ET.SubElement(definitions, Q + 'UnitTest', id=identity, name=name)
            ET.SubElement(definition, Q + 'Execution', id=identity)
            group, member = method.rsplit('.', 1)
            ET.SubElement(definition, Q + 'TestMethod', className=group, name=member)
            start = 1 + index * 15
            ET.SubElement(results, Q + 'UnitTestResult', testId=identity, executionId=identity, testName=name,
                          outcome='Passed', startTime=f'2026-10-10T00:00:{start:02d}Z', endTime=f'2026-10-10T00:00:{start + 13:02d}Z')
            record = copy.deepcopy(self.record)
            record.update(verifierMethod=method, verifierCaseId=case, sourcePath=source, sourceDigest=http.digest(path.read_bytes()),
                          environment=http.SYNTHETIC_MEMORY, observations=[])
            for offset, (assertion_case, (_, control, status, assertion)) in enumerate(http.THEORY_ASSERTIONS[method].items(), 1):
                record['observations'].append({'method': 'GET', 'path': '/api/admin/invites', 'control': control,
                    'observedStatus': status, 'expectedStatus': status, 'errorCode': None, 'responseAssertion': assertion,
                    'assertionCase': assertion_case, 'observedAtUtc': f'2026-10-10T00:00:{start + offset:02d}Z'})
            records.append(record)
        ET.SubElement(ET.SubElement(root, Q + 'ResultSummary'), Q + 'Counters', total='3', passed='3', failed='0', executed='3')
        return records, ET.tostring(root)

    def finite_account(self, records, trx):
        return http.account(self.root, self.inventory, records, trx, NOW)

    def test_finite_query_cases_keep_actor_scope_and_static_policy_denial_distinct(self):
        for method, count in ((INVITE_DENIAL, 15), (INVITE_PROJECTION, 6)):
            records, trx = self.finite_fixture(method)
            result = self.finite_account(records, trx)
            self.assertEqual(count, result['observedControlCount'])
            self.assertEqual(3, result['observedVerifierCaseReceiptCount'])
            self.assertTrue(all(row['accountingOutcome'] == 'PASS' for row in result['endpoints'][0]['controls']))
            summary = result['operationEvidenceSummary']
            self.assertEqual(0, summary['observedResourceNegativeOperationCount'])
            self.assertEqual(int(method == INVITE_DENIAL), summary['observedStaticPolicyRoleNegativeOperationCount'])
            self.assertEqual(0, result['fixtureControlDimensions'][http.SYNTHETIC_MEMORY]['currentResourceRoleDenial']['observedEndpointCount'])
            self.assertEqual('UNVERIFIED', result['candidateBinding'])

    def test_theory_case_duplicate_unknown_or_cross_case_interval_is_rejected(self):
        records, trx = self.finite_fixture()
        for changed in ([*records, records[0]], [{**records[0], 'verifierCaseId': 'UNDECLARED'}, *records[1:]]):
            with self.assertRaises(ValueError):
                self.finite_account(changed, trx)
        records[0]['observations'][0]['observedAtUtc'] = records[1]['observations'][0]['observedAtUtc']
        with self.assertRaises(ValueError):
            self.finite_account(records, trx)

    def test_theory_case_missing_receipt_or_assertion_is_explicitly_unverified(self):
        records, trx = self.finite_fixture()
        result = self.finite_account(records[1:], trx)
        missing = [row for row in result['unrecordedFiniteVerifierCases'] if row['verifierMethod'] == INVITE_DENIAL]
        self.assertEqual(['EMPTY_QUERY'], [row['verifierCaseId'] for row in missing])
        records[0]['observations'].pop()
        result = self.finite_account(records, trx)
        self.assertTrue(any(row['verifierMethod'] == INVITE_DENIAL and row['verifierCaseId'] == 'EMPTY_QUERY' and
                            row['assertionCase'] == 'BETA_OWNER' for row in result['unobservedFiniteAssertionCases']))

    def test_theory_negative_cannot_borrow_another_case_scope_or_later_positive(self):
        original, trx = self.finite_fixture()
        for mutation in ('OTHER_CASE', 'OTHER_SCOPE', 'LATER_POSITIVE'):
            records = copy.deepcopy(original)
            selected = records[0]
            if mutation == 'OTHER_CASE':
                selected['observations'] = [row for row in selected['observations'] if row['control'] not in http.POSITIVE]
            elif mutation == 'OTHER_SCOPE':
                selected['observations'] = [row for row in selected['observations'] if row['assertionCase'] != 'ALPHA_PLATFORM_ADMIN']
            else:
                selected['observations'][0]['observedAtUtc'] = '2026-10-10T00:00:12Z'
            result = self.finite_account(records, trx)
            denials = [row for row in result['endpoints'][0]['controls'] if row['verifierCaseId'] == 'EMPTY_QUERY' and
                       row['assertionCase'].startswith('ALPHA') and row['control'] in http.POLICY_ROLE_CONTROLS]
            self.assertTrue(all(row['accountingOutcome'] == 'UNVERIFIED' for row in denials))

    def test_failed_theory_case_cannot_use_another_cases_passed_execution(self):
        records, trx = self.finite_fixture()
        xml = ET.fromstring(trx)
        xml.find(Q + 'Results')[0].attrib['outcome'] = 'Failed'
        xml.find(Q + 'ResultSummary/' + Q + 'Counters').attrib.update(passed='2', failed='1')
        result = self.finite_account(records, ET.tostring(xml))
        self.assertEqual('FAIL', result['executionOutcome'])
        self.assertTrue(all(row['accountingOutcome'] == 'UNVERIFIED' for row in result['endpoints'][0]['controls']
                            if row['verifierCaseId'] == 'EMPTY_QUERY'))

    def test_finite_query_source_cardinality_actor_or_generic_status_substitution_is_rejected(self):
        records, trx = self.finite_fixture()
        for field, value in (('assertionCase', 'BETA_OWNER'), ('responseAssertion', 'STATUS_ONLY'),
                             ('errorCode', 'Forbidden'), ('control', 'CURRENT_RESOURCE_ROLE_DENIED')):
            changed = copy.deepcopy(records)
            changed[0]['observations'][0][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.finite_account(changed, trx)
        path = self.root / http.METHOD_SOURCES[INVITE_DENIAL]
        path.write_bytes(path.read_bytes().replace(b'[InlineData("")]\n', b''))
        for record in records:
            record['sourceDigest'] = http.digest(path.read_bytes())
        with self.assertRaises(ValueError):
            self.finite_account(records, trx)

    def test_existing_real_transport_http_methods_have_explicit_legacy_scopes(self):
        for method in (http.MESSAGE_ROLE, http.MESSAGE_CATCH_UP):
            record, trx = self.extra_fixture(method)
            result = self.account(record, trx)
            self.assertEqual(2, result["observedControlCount"])
            self.assertEqual(http.ENTRY_POINT, record["environment"])
            self.assertEqual("UNVERIFIED", result["candidateBinding"])
            self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_existing_http_assertions_have_explicit_memory_scopes_and_leave_provider_unverified(self):
        for method, count in zip(http.REUSED_MEMORY_METHODS, (3, 5, 4, 3, 3, 4, 3, 8, 3, 4, 9, 8, 18, 5), strict=True):
            original, trx = self.extra_fixture(method)
            result = self.account(original, trx)
            self.assertEqual(count, result["observedControlCount"])
            self.assertTrue(all(row["accountingOutcome"] == "PASS" for endpoint in result["endpoints"] for row in endpoint["controls"]))
            self.assertEqual(http.SYNTHETIC_MEMORY, original["environment"])
            self.assertEqual(0, result["controlDimensions"]["authorizedSameScope"]["observedEndpointCount"])
            self.assertEqual("UNVERIFIED", result["candidateBinding"])
            self.assertIsNone(result["ownerApproval"])
            self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])
            summary = result["operationEvidenceSummary"]
            self.assertEqual(0, summary["completeResourceContractCount"])
            self.assertEqual("UNVERIFIED", summary["allRoleOperationCoverage"])
            self.assertEqual(len(http.EXTRA_RULES[method]), summary["observedResourceNegativeOperationCount"])
            self.assertEqual(0, summary["withoutObservedResourceNegativeOperationCount"])
            self.assertEqual(0, summary["resourceNegativeOperationCountByEnvironment"][http.ENTRY_POINT])
            for change in ({"responseAssertion": "STATUS_ONLY"}, {"errorCode": "ValidationFailed"}):
                invalid = copy.deepcopy(original)
                next(row for row in invalid["observations"] if row.get("responseAssertion") is not None).update(change)
                with self.subTest(method=method, change=change), self.assertRaises(ValueError):
                    self.account(invalid, trx)
            original["observations"] = [row for row in original["observations"] if row["control"] not in http.POSITIVE]
            result = self.account(original, trx)
            self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for endpoint in result["endpoints"] for row in endpoint["controls"]))
            self.assertEqual(0, result["operationEvidenceSummary"]["observedResourceNegativeOperationCount"])

    def test_private_participant_controls_require_prior_same_operation_positive_and_exact_assertions(self):
        for method in http.PRIOR_OPERATION_POSITIVE_METHODS:
            original, trx = self.extra_fixture(method)
            for change in ({"observedAtUtc": "2026-10-10T00:00:31Z"}, {"observedAtUtc": "2026-10-10T00:00:30Z"}):
                changed = copy.deepcopy(original)
                for row in changed["observations"]:
                    if row["control"] == "AUTHORIZED_SAME_SCOPE":
                        row.update(change)
                result = self.account(changed, trx)
                denials = [row for endpoint in result["endpoints"] for row in endpoint["controls"]
                           if row["control"] in http.RESOURCE_CONTROLS]
                self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for row in denials))
                self.assertEqual(0, result["operationEvidenceSummary"]["observedResourceNegativeOperationCount"])
            for change in ({"responseAssertion": "STATUS_ONLY"}, {"observedStatus": 403, "expectedStatus": 403},
                           {"errorCode": "Forbidden"}):
                changed = copy.deepcopy(original)
                next(row for row in changed["observations"] if row["control"] in http.RESOURCE_CONTROLS).update(change)
                with self.subTest(method=method, change=change), self.assertRaises(ValueError):
                    self.account(changed, trx)

    def test_revoked_follow_up_filtered_success_is_bound_to_private_state_assertion(self):
        record, trx = self.extra_fixture(http.FOLLOW_UPS)
        result = self.account(record, trx)
        page = next(endpoint for endpoint in result["endpoints"] if endpoint["method"] == "GET")
        denial = next(row for row in page["controls"] if row["control"] == "CURRENT_CONVERSATION_AUTHORITY_REVOKED")
        self.assertEqual(200, denial["observedStatus"])
        self.assertEqual("PASS", denial["accountingOutcome"])
        self.assertEqual(3, result["operationEvidenceSummary"]["observedResourceNegativeOperationCount"])
        self.assertEqual("UNVERIFIED", page["resourceCoverageOutcome"])

    def test_core_negative_cannot_borrow_another_operation_positive_or_unasserted_membership_status(self):
        original, trx = self.extra_fixture(http.CORE_READS)
        for key in http.EXTRA_RULES[http.CORE_READS]:
            changed = copy.deepcopy(original)
            changed["observations"] = [row for row in changed["observations"]
                if not ((row["method"], row["path"]) == key and row["control"] == "AUTHORIZED_SAME_SCOPE")]
            result = self.account(changed, trx)
            selected = next(endpoint for endpoint in result["endpoints"] if (endpoint["method"], endpoint["path"]) == key)
            self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for row in selected["controls"]
                                if row["control"] in http.RESOURCE_CONTROLS))
        changed = copy.deepcopy(original)
        row = next(row for row in changed["observations"] if row["path"] == "/api/workspaces/{workspaceId}/groups" and
                   row["control"] == "CROSS_TENANT")
        row["control"] = "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED"
        with self.assertRaises(ValueError):
            self.account(changed, trx)

    def test_messaging_producer_negative_requires_exact_no_effects_assertion_and_operation_positive(self):
        record, trx = self.extra_fixture(http.MESSAGE_PRODUCER)
        result = self.account(record, trx)
        self.assertEqual(12, result["observedControlCount"])
        self.assertTrue(all(row["accountingOutcome"] == "PASS" for endpoint in result["endpoints"] for row in endpoint["controls"]))
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])
        for change in ({"responseAssertion": "STATUS_ONLY"}, {"errorCode": "Forbidden"}, {"observedStatus": 403, "expectedStatus": 403}):
            invalid = copy.deepcopy(record)
            next(row for row in invalid["observations"] if row["control"] == "CURRENT_CONVERSATION_AUTHORITY_REVOKED").update(change)
            with self.assertRaises(ValueError):
                self.account(invalid, trx)
        record["observations"] = [row for row in record["observations"] if row["control"] not in http.POSITIVE]
        result = self.account(record, trx)
        self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for endpoint in result["endpoints"] for row in endpoint["controls"]))

    def test_legacy_denial_body_or_status_cannot_substitute_for_reviewed_assertion(self):
        for method in (http.MESSAGE_ROLE, http.MESSAGE_CATCH_UP):
            original, trx = self.extra_fixture(method)
            for change in ({"responseAssertion": "REQUEST_FAILED"}, {"errorCode": "Forbidden"},
                           {"observedStatus": 404, "expectedStatus": 404}):
                record = copy.deepcopy(original)
                record["observations"][1].update(change)
                with self.assertRaises(ValueError):
                    self.account(record, trx)

    def test_domain_producer_no_effects_assertions_cannot_be_replaced_by_status_only(self):
        for method, count in ((http.DOMAIN_PRODUCER, 15), (http.COMMUNICATION_PRODUCER, 7)):
            original, trx = self.extra_fixture(method)
            result = self.account(original, trx)
            self.assertEqual(count, result["observedControlCount"])
            self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])
            for change in ({"responseAssertion": None}, {"responseAssertion": "STATUS_ONLY"}, {"errorCode": "ValidationFailed"}):
                record = copy.deepcopy(original)
                next(row for row in record["observations"] if row.get("responseAssertion") is not None).update(change)
                with self.subTest(method=method, change=change), self.assertRaises(ValueError):
                    self.account(record, trx)
            original["observations"] = [row for row in original["observations"] if row["control"] not in http.POSITIVE]
            result = self.account(original, trx)
            self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for endpoint in result["endpoints"] for row in endpoint["controls"]))

    def test_legacy_current_denial_requires_same_operation_positive(self):
        for method in (http.MESSAGE_ROLE, http.MESSAGE_CATCH_UP):
            record, trx = self.extra_fixture(method)
            record["observations"] = record["observations"][1:]
            result = self.account(record, trx)
            self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED"
                for endpoint in result["endpoints"] for row in endpoint["controls"]))

    def test_positive_observations_leave_full_contract_and_approval_pending(self):
        result = self.account()
        self.assertEqual(3, result["observedControlCount"])
        self.assertTrue(all(row["accountingOutcome"] == "PASS" for row in result["endpoints"][0]["controls"]))
        self.assertEqual("UNVERIFIED", result["candidateBinding"])
        self.assertEqual("UNVERIFIED", result["endpoints"][0]["resourceCoverageOutcome"])
        self.assertEqual([], result["endpoints"][0]["specIds"])
        self.assertIsNone(result["ownerApproval"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])
        self.assertEqual("SIX_ASSEMBLY_LOCAL_BYTES_RECONCILED", result["fullDependencyQualification"])

    def test_historical_five_assembly_recording_cannot_qualify_full_dependencies(self):
        historical = copy.deepcopy(self.record)
        historical["schemaVersion"] = 1
        historical.pop("assemblyBindingScope")
        historical["assemblyDigests"].pop("Coglatas.SecurityArchitecture")
        original = copy.deepcopy(historical)
        result = self.account(historical)
        self.assertEqual("UNVERIFIED", result["fullDependencyQualification"])
        self.assertEqual([1], result["inputReceiptSchemaVersions"])
        self.assertEqual(original, historical)

    def test_changed_or_missing_copied_verifier_and_product_dependency_are_rejected(self):
        for name in ("Coglatas.SecurityArchitecture", "Coglatas.Web"):
            path = loaded_assembly_path(self.root, name)
            original = path.read_bytes()
            for mutation in ("changed", "missing"):
                with self.subTest(name=name, mutation=mutation):
                    if mutation == "changed":
                        path.write_bytes(b"changed actual dependency")
                    else:
                        path.unlink()
                    with self.assertRaises((ValueError, OSError)):
                        self.account()
                    path.write_bytes(original)

    def test_version_scope_and_partial_receipt_cannot_claim_six_assembly_qualification(self):
        for mutation in ("missing-tool", "legacy-scope", "schema", "boolean-schema", "legacy-full"):
            record = copy.deepcopy(self.record)
            if mutation == "missing-tool": record["assemblyDigests"].pop("Coglatas.SecurityArchitecture")
            elif mutation == "legacy-scope": record["assemblyBindingScope"] = "HISTORICAL_FIVE_ASSEMBLIES"
            elif mutation == "schema": record["schemaVersion"] = 3
            elif mutation == "boolean-schema": record["schemaVersion"] = True
            else:
                record["schemaVersion"] = 1
                record["assemblyDigests"].pop("Coglatas.SecurityArchitecture")
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                self.account(record)

    def test_independent_exact_identity_reconciliation_does_not_create_approval(self):
        result = self.account(receipt=self.receipt, identity=self.identity)
        self.assertEqual("EXACT_RECEIPT_RECONCILED_TRUSTED_ATTESTATION_PENDING", result["candidateBinding"])
        self.assertEqual("UNVERIFIED", result["trustedAttestation"])

    def test_names_and_source_references_without_observations_receive_no_credit(self):
        record = copy.deepcopy(self.record)
        record["observations"] = []
        result = self.account(record)
        self.assertEqual(0, result["observedControlCount"])
        self.assertEqual(1, result["anonymousOutstandingEndpointCount"])

    def test_missing_positive_cannot_qualify_negative(self):
        record = copy.deepcopy(self.record)
        record["observations"].pop(0)
        result = self.account(record)
        self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for row in result["endpoints"][0]["controls"]))

    def test_failed_or_disabled_execution_cannot_qualify_observations(self):
        for outcome in ("Failed", "NotExecuted"):
            with self.subTest(outcome=outcome):
                result = self.account(trx=execution(outcome))
                self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for row in result["endpoints"][0]["controls"]))

    def test_missing_execution_cannot_qualify_observations(self):
        trx = ET.fromstring(self.trx)
        definition = trx.find("t:TestDefinitions/t:UnitTest/t:TestMethod", http.NS)
        definition.attrib["name"] = "DeletedVerifier"
        for row in (trx.find("t:TestDefinitions/t:UnitTest", http.NS), trx.find("t:Results/t:UnitTestResult", http.NS)):
            row.attrib["name" if row.tag.endswith("UnitTest") else "testName"] = definition.attrib["className"] + ".DeletedVerifier"
        with self.assertRaises(ValueError):
            self.account(trx=ET.tostring(trx))

    def test_wrong_candidate_attempt_build_or_artifact_rejected(self):
        mutations = {"candidateSha": "c" * 40, "runAttempt": "3", "runId": "18", "environmentFingerprint": "c" * 64,
                     "buildStampMatchesCandidate": False, "executionDigest": "c" * 64}
        for key, value in mutations.items():
            receipt = copy.deepcopy(self.receipt)
            receipt[key] = value
            with self.subTest(field=key), self.assertRaises(ValueError):
                self.account(receipt=receipt, identity=self.identity)
        with self.assertRaises(ValueError):
            self.account(receipt=self.receipt)

    def test_source_weakening_deleted_test_wrong_assembly_and_inventory_rejected(self):
        for field, value in (("sourceDigest", "c" * 64), ("sourcePath", "../outside"),
                             ("verifierMethod", http.GANTT + "Renamed")):
            record = copy.deepcopy(self.record)
            record[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record)
        record = copy.deepcopy(self.record)
        record["assemblyDigests"]["Coglatas.Web"] = "c" * 64
        with self.assertRaises(ValueError):
            self.account(record)
        self.inventory["webAssemblyDigest"] = "c" * 64
        with self.assertRaises(ValueError):
            self.account()

    def test_unrelated_permission_or_model_error_is_not_authentication_denial(self):
        for status in (400, 403, 404, 415, 500):
            record = copy.deepcopy(self.record)
            record["observations"][1].update(observedStatus=status, expectedStatus=status)
            with self.subTest(status=status), self.assertRaises(ValueError):
                self.account(record)

    def test_untyped_resource_denial_is_rejected(self):
        record = copy.deepcopy(self.record)
        record["observations"][2]["errorCode"] = "ValidationFailed"
        with self.assertRaises(ValueError):
            self.account(record)
        for field in ("observedStatus", "expectedStatus"):
            record = copy.deepcopy(self.record)
            record["observations"][1][field] = 401.0
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record)

    def test_application_owned_authentication_requires_typed_error(self):
        record = copy.deepcopy(self.record)
        record["verifierMethod"] = http.GANTT_COMMANDS
        source = http.METHOD_SOURCES[http.GANTT_COMMANDS]
        (self.root / source).write_bytes(("public async Task " + http.GANTT_COMMANDS.rsplit(".", 1)[1] + "() { }").encode())
        record.update(sourcePath=source, sourceDigest=http.digest((self.root / source).read_bytes()))
        self.inventory["endpoints"][0].update(method="PATCH", normalizedPath="/api/tasks/{taskItemId}/schedule")
        record["observations"].pop()
        for row in record["observations"]:
            row.update(method="PATCH", path="/api/tasks/{taskItemId}/schedule")
        record["observations"][1].update(control="ANONYMOUS_WITH_VALID_CSRF", errorCode="GANTT_CSRF_REQUIRED")
        trx = ET.fromstring(self.trx)
        definition = trx.find("t:TestDefinitions/t:UnitTest", http.NS)
        definition.attrib["name"] = http.GANTT_COMMANDS
        definition.find("t:TestMethod", http.NS).attrib["name"] = http.GANTT_COMMANDS.rsplit(".", 1)[1]
        trx.find("t:Results/t:UnitTestResult", http.NS).attrib["testName"] = http.GANTT_COMMANDS
        record["observations"][1]["errorCode"] = "GANTT_AUTHENTICATION_REQUIRED"
        self.assertTrue(all(row["accountingOutcome"] == "PASS" for row in self.account(record, trx=ET.tostring(trx))["endpoints"][0]["controls"]))
        record["observations"][1]["errorCode"] = "GANTT_CSRF_REQUIRED"
        with self.assertRaises(ValueError):
            self.account(record, trx=ET.tostring(trx))

    def test_wrong_endpoint_method_control_and_timestamp_rejected(self):
        for field, value in (("path", "/api/unknown"), ("method", "POST"), ("control", "NOT_APPLICABLE"),
                             ("observedAtUtc", "2026-10-10T00:01:00Z"), ("observedStatus", 403)):
            record = copy.deepcopy(self.record)
            record["observations"][2][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record)

    def test_duplicate_observations_or_receipts_cannot_multiply_coverage(self):
        record = copy.deepcopy(self.record)
        record["observations"].append(copy.deepcopy(record["observations"][2]))
        with self.assertRaises(ValueError):
            self.account(record)
        with self.assertRaises(ValueError):
            http.account(self.root, self.inventory, [self.record, self.record], self.trx, NOW)

    def test_unauthenticated_approval_fields_are_rejected(self):
        record = copy.deepcopy(self.record)
        record["ownerApproval"] = {"approved": True, "actor": "owner"}
        with self.assertRaises(ValueError):
            self.account(record)

    def test_each_reviewed_fixture_has_explicit_scopes_and_separate_provider_accounting(self):
        for method in http.EXTRA_RULES:
            with self.subTest(method=method):
                if method in http.THEORY_CASES:
                    records, trx = self.finite_fixture(method)
                    result = self.finite_account(records, trx)
                else:
                    record, trx = self.extra_fixture(method)
                    result = self.account(record, trx)
                controls = [control for row in result["endpoints"] for control in row["controls"]]
                self.assertTrue(all(control["accountingOutcome"] == "PASS" for control in controls))
                self.assertTrue(all(control["environment"] == http.METHOD_ENVIRONMENTS[method] for control in controls))
                self.assertFalse(any(row["verifierMethod"] == method for row in result["unobservedScopedControls"]))
                if http.METHOD_ENVIRONMENTS[method] in {http.COOKIE_MEMORY, http.SYNTHETIC_MEMORY}:
                    self.assertTrue(all(dimension["observedEndpointCount"] == 0 for dimension in result["controlDimensions"].values()))
                    self.assertTrue(any(dimension["observedEndpointCount"] > 0
                                        for dimension in result["fixtureControlDimensions"][http.METHOD_ENVIRONMENTS[method]].values()))

    def test_fixture_provider_or_authentication_category_cannot_be_forged(self):
        record, trx = self.extra_fixture(http.NOTIFICATIONS)
        for environment in (http.POSTGRES_COMPOSITION, http.ENTRY_POINT, http.COOKIE_MEMORY, None):
            record["environment"] = environment
            with self.subTest(environment=environment), self.assertRaises(ValueError):
                self.account(record, trx)

    def test_empty_projection_requires_exact_body_assertion_and_same_operation_positive(self):
        record, trx = self.extra_fixture(http.MY_TASKS)
        projection = next(row for row in record["observations"] if row["control"] == "CURRENT_WORKSPACE_REVOKED_EMPTY_PAGE")
        projection["responseAssertion"] = None
        with self.assertRaises(ValueError):
            self.account(record, trx)
        projection["responseAssertion"] = "TOTAL_COUNT_ZERO_AND_ITEMS_EMPTY"
        record["observations"] = [row for row in record["observations"] if row["control"] != "AUTHORIZED_SAME_SCOPE"]
        result = self.account(record, trx)
        self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for endpoint in result["endpoints"] for row in endpoint["controls"]))

    def test_disabled_or_unrecorded_fixture_controls_stay_explicitly_unverified(self):
        record, trx = self.extra_fixture(http.KANBAN_CONFIG)
        record["observations"].pop()
        result = self.account(record, trx)
        self.assertTrue(any(row["verifierMethod"] == http.KANBAN_CONFIG and row["outcome"] == "UNVERIFIED"
                            for row in result["unobservedScopedControls"]))
        self.assertIn(http.KANBAN_MOVE, result["unrecordedVerifierMethods"])

    def test_current_fact_declaration_must_exist_exactly_once_even_with_updated_digest(self):
        source = self.root / self.record["sourcePath"]
        original = source.read_bytes()
        for body in (b"// source reference only", original + original):
            source.write_bytes(body)
            record = copy.deepcopy(self.record)
            record["sourceDigest"] = http.digest(body)
            with self.subTest(body=body), self.assertRaises(ValueError):
                self.account(record)

    def test_bounded_json_rejects_duplicate_nested_fields_nonfinite_and_nonobjects(self):
        path = self.root / "evidence.json"
        for data in (b'{"ownerApproval":null,"ownerApproval":true}', b'{"row":{"observedStatus":401,"observedStatus":200}}',
                     b'{"status":NaN}', b'[]', b'[' * 2000 + b']' * 2000):
            path.write_bytes(data)
            with self.subTest(data=data[:80]), self.assertRaises(ValueError):
                http.read_json(path)
        path.write_bytes(b'{"status":401}')
        self.assertEqual({"status": 401}, http.read_json(path))
        with self.assertRaises(ValueError):
            http.read_json(path, maximum=4)

    def test_unknown_observation_payload_or_assertion_is_rejected(self):
        for field, value in (("protectedRow", "must not be copied"), ("responseAssertion", "SELF_DECLARED_PASS"),
                             ("errorCode", "unexpected positive payload")):
            record = copy.deepcopy(self.record)
            record["observations"][0][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record)

    def test_reviewed_cookie_and_resource_denials_reject_unrelated_error_causes(self):
        for method in (http.KANBAN_CONFIG, http.NOTIFICATIONS, http.EXECUTION_SCOPE, next(iter(http.COOKIE_METHODS))):
            record, trx = self.extra_fixture(method)
            negative = next(row for row in record["observations"] if row.get("errorCode") is not None)
            for field, value in (("errorCode", "ValidationFailed"), ("observedStatus", 400)):
                mutated = copy.deepcopy(record)
                index = record["observations"].index(negative)
                mutated["observations"][index][field] = value
                if field == "observedStatus":
                    mutated["observations"][index]["expectedStatus"] = value
                with self.subTest(method=method, field=field), self.assertRaises(ValueError):
                    self.account(mutated, trx)

    def test_anonymous_extra_scope_cannot_borrow_a_different_operation_positive(self):
        record, trx = self.extra_fixture(http.EXECUTION_SCOPE)
        record["observations"] = [row for row in record["observations"]
                                  if not (row["control"] == "AUTHORIZED_SAME_SCOPE" and row["method"] == "GET")]
        result = self.account(record, trx)
        anonymous = next(row for endpoint in result["endpoints"] for row in endpoint["controls"] if row["control"] == "ANONYMOUS")
        self.assertEqual("UNVERIFIED", anonymous["accountingOutcome"])

    def test_runtime_template_parameter_identity_cannot_be_silently_replaced(self):
        record, trx = self.extra_fixture(http.KANBAN_MOVE)
        for row in record["observations"]:
            row["path"] = row["path"].replace("{taskId}", "{taskItemId}")
        with self.assertRaises(ValueError):
            self.account(record, trx)


if __name__ == "__main__":
    unittest.main()
