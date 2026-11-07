# Native child Web shutdown qualification

The actual Web fixture previously killed its owned child process at disposal.
An exact-Main Linux run of the existing legacy Post mutation HTTP/PostgreSQL
test passed, while the original parent OpenCover report contained zero visits
for all 237 ChannelService sequence points. The equivalent Windows observation
is retained separately. A functional pass alone does not establish child coverage.

The fixture attempts normal termination of its own Linux x64/arm64 child through
a Linux process file descriptor. It acquires that descriptor before checking the
original Process object's retained child exit state. A reused PID is not signaled
when the original child has exited; after this check, the descriptor keeps a stable
identity through signaling. No signal helper process or inherited helper
environment remains. Unsupported platforms/kernel calls, signal failure or a
bounded host-exit timeout retain the original process-tree kill fallback.
Authentication, Hub/dispatcher services, process startup, credential/environment
isolation and existing test assertions are unchanged.

The native contract follows the Linux
[pidfd signaling documentation](https://man7.org/linux/man-pages/man2/pidfd_send_signal.2.html)
and .NET 10's retained child exit state in
[ProcessWaitState.Unix.cs](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/ProcessWaitState.Unix.cs).
Focused native controls cover a sleeping owned child, an already exited child,
and an owned child ignoring TERM that requires the force-stop fallback. These do
not exhaustively reproduce PID reuse races or prove descendant cleanup after
every graceful parent exit.

The initial `/bin/kill` candidate's native coverage observations are retained
historically. Independent review identified PID reuse and a potentially delayed
helper after timeout; those observations do not qualify this revised source.

Qualification must compare exact-source before/after actual HTTP tests on the
same pinned Linux SDK/PostgreSQL and original native collector. Report bytes,
source ranges, zeros, executed DLL/PDB identities and the raw imported report
must remain inspectable. No report editing, synthetic hits, new imports, narrowed
source scope, baseline or threshold changes are allowed. A local result does not
establish resulting-Main coverage; measure the resulting Main independently.

This is test process cleanup and native collection infrastructure, not a product
shutdown endpoint, production operation, authorization/RLS change, deployment
acceptance, security disposition or Release GO. A graceful-stop attempt is not
proof that every child exits gracefully or that a previously unknown failure's
initiating cause is resolved.
