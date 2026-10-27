"""Exercise fixed identity, closed output, source binding and refusal of redirects."""
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest
import urllib.error

spec = importlib.util.spec_from_file_location("state_probe", Path(__file__).with_name("licensed-workflow-state.py"))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)
SECRET = "SYNTHETIC_PRIVATE_TOKEN_AND_BODY"
SHA = "a" * 40
ENV = dict(GITHUB_SHA=SHA, GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1", GH_TOKEN=SECRET)


def raw(**changes):
    data = dict(id=probe.WORKFLOW_ID, path=probe.WORKFLOW_PATH, state="active")
    data.update(changes)
    return json.dumps(data).encode()


def git(arguments, **kwargs):
    return (SHA + "\n").encode() if arguments[1] == "rev-parse" else b""


class Response:
    def __init__(self, body, status=200, url=probe.ENDPOINT):
        self.body, self.status, self.url = body, status, url

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False

    def geturl(self):
        return self.url

    def read(self, size):
        return self.body[:size]


class Opener:
    def __init__(self, response=None, error=None):
        self.response, self.error, self.calls = response, error, []

    def open(self, request, **kwargs):
        self.calls.append((request, kwargs))
        if self.error:
            raise self.error
        return self.response


class WorkflowStateTests(unittest.TestCase):
    def test_valid_provider_state_is_digest_bound_and_never_execution_acceptance(self):
        response = raw()
        result, code = probe.execute(ENV, read=lambda _: probe.project(response), git=git)
        self.assertEqual(code, 0)
        self.assertEqual(result["providerState"], "active")
        self.assertEqual(result["rawResponseSha256"], hashlib.sha256(response).hexdigest())
        self.assertEqual((result["sourceHead"], result["runId"], result["runAttempt"]), (SHA, "123", "1"))
        self.assertEqual(result["functionalAcceptance"], "NOT_EVALUATED")
        self.assertFalse(result["policyChanged"])
        self.assertEqual(result["licenseAvailability"], "UNKNOWN")
        self.assertEqual(result["workflowSelectionCause"], "UNKNOWN")

    def test_every_closed_provider_state_remains_a_provider_statement_only(self):
        for state in probe.STATES:
            with self.subTest(state=state):
                self.assertEqual(probe.project(raw(state=state))["providerState"], state)

    def test_wrong_id_bool_id_path_and_unknown_state_are_refused(self):
        for change in (dict(id=1), dict(id=True), dict(id="348966932"),
                       dict(path="../" + SECRET), dict(path=None), dict(state=SECRET),
                       dict(state=None), dict(state=[])):
            with self.subTest(change=list(change)):
                with self.assertRaises((probe.Refused, ValueError)):
                    probe.project(raw(**change))

    def test_malformed_duplicate_nonfinite_and_unbounded_json_are_refused(self):
        for response in (b"\xff", b"{", b"[]", b'{"id":1,"id":2}', b'{"id":NaN}',
                         b"x" * 65537):
            with self.subTest(size=len(response)):
                with self.assertRaises((ValueError, UnicodeError)):
                    probe.project(response)

    def test_unrecognized_provider_fields_are_not_republished(self):
        result = probe.project(raw(name=SECRET, url=SECRET, arbitrary=dict(body=SECRET)))
        rendered = json.dumps(result)
        self.assertNotIn(SECRET, rendered)
        self.assertEqual(set(result), {"workflowId", "providerState", "rawResponseSha256", "responseBytes"})

    def test_transport_uses_only_fixed_api_and_builtin_token_without_redirects(self):
        opener = Opener(Response(raw()))
        result = probe.fetch(SECRET, opener)
        self.assertEqual(result["providerState"], "active")
        self.assertEqual(len(opener.calls), 1)
        request, options = opener.calls[0]
        self.assertEqual(request.full_url, probe.ENDPOINT)
        self.assertEqual(request.get_header("Authorization"), "Bearer " + SECRET)
        self.assertEqual(options["timeout"], 30)
        self.assertIsNone(probe.NoRedirect().redirect_request(request, None, 302, SECRET, {}, "https://" + SECRET))

    def test_redirects_and_non_200_responses_are_refused_without_secondary_requests(self):
        for status in (301, 302, 307, 308, 401, 403, 500):
            opener = Opener(error=urllib.error.HTTPError(probe.ENDPOINT, status, SECRET,
                                                        {"Location": "https://" + SECRET}, None))
            with self.subTest(status=status):
                with self.assertRaises(probe.Refused):
                    probe.fetch(SECRET, opener)
                self.assertEqual(len(opener.calls), 1)
        for response in (Response(raw(), 201), Response(raw(), url="https://" + SECRET)):
            with self.assertRaises(probe.Refused):
                probe.fetch(SECRET, Opener(response))

    def test_missing_token_is_refused_before_network(self):
        opener = Opener(Response(raw()))
        with self.assertRaises(probe.Refused):
            probe.fetch("", opener)
        self.assertEqual(opener.calls, [])

    def test_execution_binding_rejects_invalid_identity_before_any_api_read(self):
        for changes in (dict(GITHUB_SHA=SECRET), dict(GITHUB_RUN_ID="0"),
                        dict(GITHUB_RUN_ATTEMPT=SECRET), dict(GITHUB_RUN_ATTEMPT="0")):
            environment = {**ENV, **changes}
            calls = []
            result, code = probe.execute(environment, read=lambda _: calls.append(True), git=git)
            self.assertEqual(code, 1)
            self.assertEqual(calls, [])
            self.assertNotIn(SECRET, json.dumps(result))

    def test_stale_or_dirty_checkout_is_refused_before_api_read(self):
        def stale(arguments, **kwargs):
            return (b"b" * 40 + b"\n") if arguments[1] == "rev-parse" else b""
        def dirty(arguments, **kwargs):
            return (SHA + "\n").encode() if arguments[1] == "rev-parse" else SECRET.encode()
        for checkout in (stale, dirty):
            result, code = probe.execute(ENV, read=lambda _: self.fail("API was called"), git=checkout)
            self.assertEqual(code, 1)
            self.assertEqual(result["failure"], "UNCLEAN_OR_STALE_SOURCE")
            self.assertNotIn(SECRET, json.dumps(result))

    def test_library_and_unrecognized_refusal_messages_cannot_leak(self):
        for failure in (RuntimeError(SECRET), probe.Refused(SECRET)):
            def read(_):
                raise failure
            result, code = probe.execute(ENV, read=read, git=git)
            self.assertEqual(code, 1)
            self.assertEqual(result["failure"], "UNCLASSIFIED_READ_FAILURE")
            self.assertNotIn(SECRET, json.dumps(result))

    def test_api_failure_has_no_provider_state_or_acceptance_credit(self):
        def read(_):
            raise probe.Refused("PROVIDER_STATUS")
        result, code = probe.execute(ENV, read=read, git=git)
        self.assertEqual(code, 1)
        self.assertNotIn("providerState", result)
        self.assertEqual(result["verification"], "NOT_VERIFIED")
        self.assertEqual(result["licenseAvailability"], "UNKNOWN")


if __name__ == "__main__":
    unittest.main()
