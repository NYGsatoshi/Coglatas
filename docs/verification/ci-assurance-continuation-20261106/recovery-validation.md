# Decision recovery validation and implementation boundary

The recovery worktree was clean and matched Draft #1747 head
`1b01e4eede110660b96e2a3ab691f0dc0d2e196e` before editing. Its base was
Security `3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`. The original workspace's
six unrelated untracked image receipts/reports were preserved. #1749 remains
independent at `fe059005a9e97af3f28db68120331b5b8a983e99`.

This slice adds the approved decision wording, full PR-N contract, independent
state/HOLD register, original-evidence preservation anchors and executable
documentation validation. It appends to the checkpoint README. It changes no
workflow, required-check contract, governance policy/settings, scanner threshold,
product behavior, release authorization, migration, snapshot or baseline.

Focused Windows validation passed the contract/preservation CLI and all eleven
test methods, including negative mutations of decision wording, deliverables,
rejection controls, exit criteria, authority bits, all eleven completion states,
holds, JSON shapes, references and historical digests. The existing publication,
required-check and content-policy validators passed before the first recovery
commit. All new prose is English; intentional Unicode punctuation/identifiers
are not translated diagnostic evidence or product localization changes.

The first Linux cross-platform probe rejected a retained JSON checkout identity:
Linux Git lacked the Windows global CRLF clean setting. This was a validation
portability failure, not evidence that the retained Git blob changed. The earlier
failure remains in the session transcript. The validator now requests a
per-command `core.autocrlf=input` clean conversion, respects path attributes and
compares canonical Git object identity to the pinned source. It changes no file,
raw artifact, repository/global setting, byte-digest requirement for native
artifacts, or original evidence. With that explicit canonical-source handling,
Linux contract/preservation validation and all eleven methods passed. The
original ledger still contains exactly 45 capabilities and all fifteen
preservation anchors verify.

The default Ubuntu-22.04 review environment still fails with the original
`Wsl/Service/CreateInstance/MountDisk/HCS/ERROR_PATH_NOT_FOUND`. Investigation
found an existing usable Ubuntu distribution with authenticated CodeRabbit
0.9.2. Explicit distribution selection and per-process Git worktree paths restore
CLI access without changing WSL registration, disk files, defaults, credentials
or GitHub settings. CLI access does not establish independent review completion.
Actual review results and final-head native run identities belong in the PR
qualification record, preserving all earlier failed/cancelled attempts.

The dependency-ready PR-N documentation/reference/state/preservation validator
is implemented and locally validated. The cross-domain final evidence verifier
remains unimplemented; neither local tests nor native check success grant final
coverage, provenance, review, governance, security, performance, Nightly, release
or Owner approval credit. The next independent implementation slice remains
the bounded PR-J original-case/native-case reconciliation adapter described in
[the PR-N contract](pr-n-contract.md). Integration stays HOLD.

Fresh CodeRabbit review of `23384803e47c36405820943c592006e7f10ded28`
completed with one minor table-scope ambiguity; an append-only clarification
resolved it. Review of `1023f1c488468f4580de4d04f8f6da3b3c080bd4` completed with
two minor issues: reference-scope handling and historical-object prerequisites.
The contract now explicitly defines local-file-only references and tests rejection
of unsupported targets, preserving fail-closed behavior rather than skipping
unknown references. It also documents exact-SHA read-only fetch prerequisites
and forbids skipping preservation checks when objects are absent. The original
review outputs remain retained locally. Decision wording checks additionally
cover entire decision bodies and reject appended unauthorized instructions.

Review of `54e7c9d0611f31d0a88e8ddb60bacc4fd3fe4864` completed with one minor
newline-symmetry issue in the append-only comparison. Although the pinned
checkpoint blob contains LF and passed both hosts, the implementation now
compares byte prefixes with explicit CRLF clean handling on both sides. Focused
controls retain valid LF/CRLF checkouts and reject changed failure wording,
truncation and prepending. The original canonical blob digest is still checked
first; raw native artifact digest handling and source bytes remain unchanged.
