# Socket SCA installation integrity

Related: #614, #615. This is a bounded installation change, not scanner
activation, an exception approval, a complete security gate or release GO.

The existing optional trusted-Main workflow resolved its Python CLI dependencies
at execution time. Bind the existing tool to reviewed wheel bytes instead:
`scripts/ci/socket-sca-requirements.txt` contains 20 exact direct wheel URLs with
SHA-256 hashes, including Socket CLI 2.11.6 and its complete default transitive
closure for CPython 3.12 / Linux x86_64. The previous floating installation has
no recorded resolved version here; this is not a claim that 2.11.6 was previously
qualified or that any package was compromised.

Use the existing Python 3.12 in an isolated job-local virtual environment.
Installation requires hashes and binary distributions, checks dependency
consistency and CLI version before scan credentials, then invokes that exact
virtual environment's CLI. Missing or incorrect wheel bytes fail the step.
Lockfile changes select the existing trusted-Main workflow. Opt-in variable,
protected environment, permission, timeout, scan arguments and scan exit-code
propagation are unchanged. No scan credential is copied to a PR workflow.

## Executed controls

- Resolve and hash-bind all 20 wheels for the actual CPython/Linux target.
- Install the original complete lock under Python 3.12; `pip check` passes.
- With Git available, `socketcli --version` reports 2.11.6 and `--help` succeeds
  without credentials. Existing `SOCKET_SECURITY_API_KEY`, `GH_API_TOKEN`,
  `--target-path` and `--scm github` remain supported by the inspected wheel.
- Replace one expected SHA-256 with zeros: pip rejects the actual wheel with
  a hash mismatch and exits 1, before installation or credentialed execution.
- Remove the transitive `smmap` lock entry: pip rejects the unresolved range in
  require-hashes mode and exits 1 instead of silently selecting an unreviewed
  transitive package.

The initial slim Python fixture lacked Git, so CLI imports failed. Its shell
did not propagate those intermediate failures; the final zero exit was **not**
accepted as a passing CLI control. The corrected disposable fixture installs
Git and uses a strict shell; version/help and dependency checks pass. No import
warning is suppressed and no live scan is substituted for the failed fixture.

Fixture base: `python:3.12-slim-bookworm`, immutable digest
`sha256:e697c81897302a4c4333bade000d98038fbb21eb064f8ee1fd42d02ebcd9d337`.
No .NET, Node or Ubuntu baseline changes are included. The optional authenticated
Socket scan remains NOT_EXECUTED locally. Its existing opt-in and protected
environment govern any future real execution. Independent review, exact-head
CI/official Sonar analysis and normal merge/Main qualification are still needed.

The related public API/provider findings are not manually dismissed by this
patch. Provider closure requires the new exact-source analysis. Owner decisions
and all held/excluded PRs remain unchanged. **RELEASE_NO_GO.**
