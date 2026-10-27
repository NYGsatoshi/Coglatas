# SEC-17 realtime finding bridge: inert review candidate

This is a source and schema proposal for public #690, #582 and #695. It is
CANDIDATE_NOT_ADOPTED. It does not normalize a native result, assign real severity,
activate policy, approve exceptions, complete SEC-17, or change any existing test,
workflow, policy, required check or production authorization.

The existing common SEC-08 model already owns safe metadata normalization,
fingerprints, execution failure and non-suppressible authorization classes.
Its v1 scope is an exact OpenAPI HTTP operation. A SignalR event delivery must
not acquire a fabricated HTTP method or route to fit that model. The standalone
candidate reuses its closed-field validator and domain-separated digest helper;
it does not replace its governance or modify HTTP v1 behavior.

The proposed finite domain is one existing direct Workspace File policy scenario:
Files.FileChanged.v1 on /hubs/app after Private policy commit or immutable fixture
replay, observed on an established or fresh ordinary-member socket. Scope uses
logical category, resource class, phase and receiver class. It never accepts
Tenant, resource, event or user IDs, payload, raw error, body, URL or credentials.
The proposed fingerprint has a distinct unadopted domain and is not an SEC-08
finding fingerprint. No severity value is selected. The ordinary-workspace-member role is an explicitly unadopted logical scope
label. It does not reuse the different HTTP ReadOnly fixture's role alias; a
future canonical adapter must review the actual ordinary-member mapping.

DECLARED_COMPLETE output states do not establish native execution.
Declared complete observations require every finite category and confirmed
positive controls. Missing execution, incomplete inventory or unconfirmed
liveness remains EXECUTION_FAILURE. Partial delivery observations survive later
execution failure as untrusted proposals; neither turns into a finding.
Unknown fields/categories and duplicate rows are rejected using fixed errors.
Every output fixes liveGateQualified=false, producerVerified=false,
severity=null, normalizedFinding=null and policyDecision=null. Empty proposals
are not zero normalized findings or a policy PASS.

The focused controls cover finite observations, incomplete/native failure,
unknown protected canaries, stable and distinct proposed fingerprints, and
existing HTTP v1 identity bytes plus unchanged authorization blocking. The
Informational severity in the HTTP regression fixture is synthetic and tests
the existing severity-independent class rule; it assigns no real finding risk.
The candidate has no CLI, native observation producer, uploader or CI integration.
Controls are prepared for review; this source artifact does not claim they ran.

Before adoption, independently review the exact canonical realtime scope and
severity/classification mapping, then the native finite-observation producer,
source/build/TRX binding, coverage, sanitizer and consumer behavior. Native test
FAIL, timeout, missing execution or parser error must stay independently nonzero;
generic TRX ErrorInfo must never be parsed into a vulnerability. A real violation
requires a source-observed access/delivery oracle with live positive controls.
Existing policy stays NOT_ACTIVATED, initial inventory UNREVIEWED, with no real
exceptions. Medium inventory review, exception approval and live activation are
separate decisions; this proposal neither reopens settled authorization semantics
nor substitutes an unrelated release gate.
