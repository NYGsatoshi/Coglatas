# Repository Git executable boundary

The Security Architecture CLI reads immutable Git objects from specification and
implementation repositories. Revision and source-path validation protects Git's
arguments; it does not select a trusted executable. Git is now selected from
explicit, fully qualified host PATH entries, without the operating system's
implicit current-directory or application-directory search. Empty and relative
entries cannot select a command. Windows quoted installation paths remain valid.

The inspected repository cannot supply Git through a direct PATH entry, a nested
directory, or a resolved filesystem alias. Unix resolves paths with realpath
before the containment check, including symlink/parent traversal order, and
requires an executable file. Windows compares the opened repository identity
with the executable's actual ancestor identities using the volume serial and
128-bit file ID. This preserves local, junction, UNC and mapped-drive forms
without requiring a volume GUID for network shares. The ordinary absolute
Windows launch path is reopened and checked against the original file identity.
Unresolvable entries fail closed; another explicit usable host entry may follow.

On Linux, physical paths are also mapped through `/proc/self/mountinfo` to the
underlying filesystem device and mount root. Repository root binds, bound
subdirectories and aliases of mounted repository subtrees cannot become host
installations. Missing, malformed or ambiguous stacked mount mappings fail
closed. Both the physical PATH directory and executable target are checked.
The selected absolute directory retains the `git` invocation name, preserving
trusted Unix multicall shims without restoring implicit command search. Native
before/after controls distinguish executable aliases from a valid host shim.
The mount fields follow the [Linux mountinfo specification](https://man7.org/linux/man-pages/man5/proc_pid_mountinfo.5.html).
Exposed OverlayFS upper/lower layers are also treated as repository backing
storage, including nested overlays and mounted subtrees. The whole layer tree
is excluded to cover redirected or metacopy paths. A Git installation inside
such a backing layer is unsupported even if it lies outside the inspected
subdirectory. For a candidate on another filesystem, unavailable or cyclic
layer evidence fails closed rather than guessing a host identity. Git in the
same overlay view outside the repository and independent host installations
outside its layers remain eligible. Native controls retain the original
exposed-upper execution and require exact host-Git blob bytes after correction.

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
