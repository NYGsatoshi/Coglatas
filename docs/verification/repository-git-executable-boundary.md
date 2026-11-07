# Repository Git executable boundary

The Security Architecture CLI reads immutable Git objects from specification and
implementation repositories. Revision and source-path validation protects Git's
arguments; it does not select a trusted executable. Git is now selected from
explicit, fully qualified host PATH entries, without the operating system's
implicit current-directory or application-directory search. Empty and relative
entries cannot select a command. Windows quoted installation paths remain valid.

Automatic selection rejects direct repository PATH entries, nested directories
and supported resolved filesystem aliases. Unix resolves paths with realpath
before the containment check, including symlink/parent traversal order, and
requires an executable file. Windows compares the opened repository identity
with the executable's actual ancestor identities using the volume serial and
128-bit file ID. This preserves local, junction, UNC and mapped-drive forms
without requiring a volume GUID for network shares. The ordinary absolute
Windows launch path is reopened and checked against the original file identity.
Unresolvable entries fail closed; another explicit usable host entry may follow.
The original PATH directory ancestry is checked before trusting its resolved
target. A directory or file link supplied inside an inspected repository cannot
choose a different host executable. Traceability commands supply both inspected
roots to both Readers, so the specification read cannot select implementation
repository executables, or conversely.

Linux automatic selection maps physical paths through `/proc/self/mountinfo`
for ordinary filesystem and bind-mount views, including mounted repository
subtrees. Missing, malformed or ambiguous mappings fail closed. It does not
infer an OverlayFS layer identity from a reported pathname: an overmount can
hide the directory still held by OverlayFS. Overlay cross-filesystem selection
and unclassified providers therefore require an operator-selected installation.
Separate 9p or virtiofs views also require explicit operator trust: different
anonymous device identities do not prove that two exports represent different
host content. A bind view of the same export retains its device/root mapping.
The same overlay view outside the repository remains eligible automatically.
Every directory and executable link is expanded one component at a time before
checking its next target. A host entry cannot delegate its executable choice
through an intermediate link inside either inspected repository, even when the
final target is outside both roots. Legitimate host link chains and multicall
shims still retain the Git invocation name.
Both the physical PATH directory and executable target are checked, and the
selected directory retains the `git` invocation name for Unix multicall shims.
The mount fields follow the [Linux mountinfo specification](https://man7.org/linux/man-pages/man5/proc_pid_mountinfo.5.html).

An operator or trusted CI image may supply an absolute executable path through
`COGLATAS_TRUSTED_GIT_EXECUTABLE`, for example `/usr/bin/git` in the pinned SDK
image. This is explicit trust configuration, independent of the inspected
repository and its filesystem-provider metadata. Never populate it from source
declarations, repository configuration or untrusted artifact input. The selected
host installation and a stable host filesystem are the operator's trust inputs;
this mode does not claim to infer every opaque alias. Direct repository paths
and symbolic-link aliases resolving to an inspected root reject; opaque bind
aliases remain part of the operator's installation trust decision. Empty, relative, unavailable or
non-executable configuration fails without falling back to PATH. Existing blob
arguments, size/time limits, output privacy and sanitized error handling remain
unchanged. Linux container callers inspecting a bind-mounted repository may
need this explicit configuration when host Git is in an overlay root filesystem.
Native controls distinguish automatic denial from operator-selected exact-blob
host Git, and retain the original visible and hidden overlay failures.

Host PATH installation directories and a stable host filesystem remain trusted
inputs. The check is not an atomic executable-handle launch, a signature policy,
or protection against an attacker replacing a trusted installation concurrently.
It does not attest arbitrary filesystem providers. No Git arguments, source
grammar, blob size limit, timeout, stderr privacy, missing-object behavior or
sanitized CLI error contract was weakened.

`RepositoryGitExecutableTests` participates in the existing three-OS portability
matrix and backend CI. It checks explicit selection, aliases, relative repository
roots, filesystem roots, unavailable entries, and an actual immutable blob read
with a repository-local executable present. Existing registry/history/traceability
and oversize-blob tests remain unchanged. Separate isolated-process reproductions
exercise actual Reader invocation under hostile current directories and PATH
entries; they preserve original unsafe observations and original output bytes.
The local tool controls use the Tools namespace; the exact formal SEC-ARCH
evidence catalogue and producer remain unchanged. Tool-only PR routing selects
both owners, including when no test file changes, with a dedicated regression.
Main push portability paths also include the tool directory, so a later tool-only
change automatically runs Windows, macOS and Linux. The existing workflow
contract rejects removal of that path or its Main branch trigger; the matrix,
portable cohort and result thresholds remain unchanged.

The initial candidate's Windows network-share rejection was independently
identified and reproduced against an accessible localhost synthetic fixture.
That candidate used a volume GUID query unavailable on SMB. The corrected
identity-based check must also reject local/UNC aliases of repository executables
while preserving a legitimate network checkout and host installation. Localhost
SMB evidence is distinct from arbitrary remote-server or deployment acceptance.

This changes a local CLI executable-selection boundary. It is not evidence of an
HTTP authorization compromise, a formal finding disposition, a waiver, Pilot
acceptance or Release GO. Current-head checks, official PR Gate and independent
review remain prerequisites for normal merge; resulting Main is measured again.
