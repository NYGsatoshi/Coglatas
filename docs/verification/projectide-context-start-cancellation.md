# ProjectIDE local context start cancellation

Existing #1030/#908/#1031/#1032 local preparation operations accept cancellation
while replacing the synthetic context. Rename, Revert, Project Test definition
apply, Scenario load and Scenario-to-Draft apply now pass that token through the
shared private context start. The initial Empty notification can cancel, reset,
dispose or begin a newer ready/waiting/refused context. An older cancelled call
throws OperationCanceledException before publishing its replacement Source,
including when a newer context has already superseded it.

The token is checked before starting the lifecycle, after the initial notification
and after materializing input, before Source/request publication. The existing
publication commit point is retained: cancellation raised by a later Waiting
notification does not roll back an already admitted context. A fresh Compiler
response is still required. No partial Source writes or durable Draft operations
are introduced; original immutable inputs, Compiler results and prepared after
inputs retain their bytes. The public BeginSynthetic and recording entry points
keep their existing default non-cancellable behavior.

Product tests use actual local Compiler hosts and checked plans for all five
operations. Initial cancellation modes include cancel-only, newer ready/waiting/
refused, reset and dispose. Separate normal and post-publication cancellation
controls retain Source publication and subsequent fresh compilation. No current
authority, actual Avalonia, independent Security, numerical assurance or server
persistence acceptance is inferred. Existing P0 #1930/#1932/#1945/#1951 retain
their runtime/independent scope. All #1144/#1128 and Registry/Trust/Eligibility/
RLS/Native/Production/Release HOLDs remain.
