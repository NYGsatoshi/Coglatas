"""Shared assertions for unavailable local SEC-17 execution inputs."""


def assert_unavailable_capture_is_closed(test, capture_module, root, candidate, now):
    invalid_input = root / "artifacts/directory.trx"
    invalid_input.mkdir()
    with test.assertRaises(ValueError):
        capture_module.capture(root, invalid_input, candidate, now)
    summary = capture_module.failure_summary(candidate, invalid_input)
    test.assertIsNone(summary["executionDigest"])
