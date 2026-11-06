# Existing Performance Python control coverage

This continuation of #1748 registers all eleven remaining existing Performance
unit-test modules in the same canonical Python coverage producer. Its original
46 suites and 60 source subjects stay in order. The expanded scope includes all
29 tracked operational Python modules in scripts/performance, adding 22 subjects,
including any zero-hit paths. Test bodies are excluded as before.

Four existing test modules lacked a direct entry point. Only unittest.main guards
are appended; every original byte of their test bodies and assertions remains.
The strict source-set control receives exactly the same 22 expected paths.
There is still one native producer and one original XML scanner input; no source
exclusion, threshold, baseline, pinned dependency or report rewrite changes.

The full eleven-module baseline uses unittest discovery, including the four
previously inert direct scripts. Its first local container profile retained 14
setup errors in the signed-bundle suite because OpenSSH requires a passwd entry
for UID1000. No cases were removed: a separate named-user image adds only that
entry to the immutable test-tools image before rerunning every module. The
ephemeral signatures are synthetic and confined to owned temporary directories;
no host keys, remote connection, actual campaign or approval registry is used.

Fresh committed-source native collection must execute every old and new suite,
validate original XML against raw/native JSON ranges and Git bytes, retain old
covered hits and all zero-hit paths, reject duplicate imports, and preserve all
original report bytes. Passing local counters are not a projected Main gain.
Full exact-head CI, official PR Gate and independent review remain mandatory.

Integration follows #1748 and its resulting-Main qualification. Reconcile #1742
Node collection if present; neither producer may duplicate another scanner input.
These are offline synthetic control tests, not actual performance captures,
hardware, real SSH/HTTPS, full recovery, deployed acceptance or Release GO.
Owner-held promotions and formal finding decisions remain separate.

Specification850a5d9376db11e75235cf55e03ebed4de9c0510; #615 Phase G.

The complete named-user baseline executes all 156 cases in the eleven existing
modules. Initial committed native collection8787e882 executes 1115 cases, but
contains only 78 of the declared 82 sources: four CLI modules were never loaded.
That incomplete report is retained and is not qualified as full source mapping.
The include scope is not reduced. Ten additional offline controls exercise
those entry points: bounded health reads and denial handling, warmup rejection
before authentication/network/output, trusted GitHub source identity and output
binding, and immutable/local-only Announcement diagnostic admission. They use
synthetic in-memory responses and temporary output files, with network disabled.
The resulting 58-suite collection must retain every previous case and report all
82 sources, including zero-hit executable paths. No report is rewritten to add
missing subjects or manufacture coverage.
