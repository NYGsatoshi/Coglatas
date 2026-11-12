# Conversation-create dialog during routine inbox catch-up

A supported same-scope inbox refresh could close an already open conversation-create
dialog. The registered Messaging catch-up callback changes the page to `loading`;
the component previously treated that temporary state as a reason to discard the
dialog. A real Angular lifecycle control reproduced the loss while the authenticated
actor and scope remained unchanged. HTTP and Realtime transport were mocked in
that control; this is not backend, RLS, device or IME acceptance evidence.

The component retains the dialog and local recipient query or Group title during a
routine refresh within the same existing list-presentation binding. Old recipient
options, selected identities and pending UI work are discarded. Lookup, selection
and submission cannot resume before a successful current inbox projection. Group
member choices are remounted against current state after that success.

An unsuccessful projection, authorization boundary, actor/Tenant/Workspace change
or route unmount still clears the old dialog and pending work. Preservation requires
both the page and the inbox request to be loading. A response for the wrong inbox
view is rejected and closes the dialog even when the page still shows loading. The binding revision
also detects a hard boundary when loading and recovery complete before rendering.
Late lookup or create responses cannot restore old identities, navigate to an old
command result or repeat a command. Unsubscribing an HTTP request does not establish
that the server rolled back a committed create; no server transaction or retry
contract changes here.

The focused regression controls include a healthy dialog, held and coalesced
successful refreshes, denial, stale callbacks before cancellation renders, actual
scope replacement, protected-state invalidation, unmount and Group picker renewal.
The test-only comparator is commit `e4110a68a28e1fede8e0baf8b3b5d80167986a14`.

The Group DM Playwright suite also contains real-browser Direct and Group dialog
controls using mocked HTTP and SignalR. They hold the inbox response after a
transport reconnect, assert disabled commands and discarded old selections, then
require a successful current projection and fresh recipient lookup or Group roster.
A denied projection closes the old dialog. These controls preserve local query or
title intent only; they do not establish server transaction outcomes, physical
device behavior, a real user's IME, or deployed authorization acceptance.

This is separate from the historical `FUNC-MSG-001 / MSG-01` timeout in CI
`41577374340/1`. That execution did not retain the dialog transition or error payload
needed to prove its cause. One isolated execution of the original source and runtime
later passed both collaboration owners at unchanged deadlines and retry zero.
The original first failure remains unresolved; this repair does not close a
historical causal-reliability issue or grant release acceptance.
