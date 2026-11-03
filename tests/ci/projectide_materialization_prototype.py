"""NON-NORMATIVE test-only Option B model. No production wire/identity/trust contract.

The tuples are examples, not serialized schemas. A matching claim never grants
producer authentication, current authorization, evidence eligibility or execution.
"""
from dataclasses import dataclass
import hashlib


@dataclass(frozen=True, order=True)
class Pin:
    kind: str
    scope: str
    revision: str
    candidate: str
    scenario: str
    version: int
    canonical: int
    digest: str


def pin(kind, content, *, scope='authorized-project', revision='r1', candidate='', scenario='', version=1, canonical=1):
    # Deliberately not Core canonicalization. ASCII test vectors only.
    digest = hashlib.sha256((f'example.NON_NORMATIVE/{kind}/{version}\0').encode() + content).hexdigest()
    return Pin(kind, scope, revision, candidate, scenario, version, canonical, digest)


@dataclass(frozen=True)
class Input:
    source: Pin
    manifest: Pin
    dependencies: tuple[Pin, ...]


@dataclass(frozen=True)
class ReceiptClaim:
    input: Input
    actual: tuple[tuple[Pin, bytes], ...]


def assess(desired, claim, *, budget=100, cancelled=lambda: False):
    findings = set()
    def spend():
        nonlocal budget
        if cancelled():
            raise InterruptedError('cancelled')
        budget -= 1
        if budget < 0:
            raise OverflowError('exhausted')
    try:
        spend()
        if desired != claim.input:
            findings.add('input-mismatch/stale')
        declared = set()
        for item in desired.dependencies:
            spend()
            key = (item.kind, item.scope, item.revision, item.candidate, item.scenario)
            if key in declared:
                findings.add('duplicate-or-conflicting-pin')
            declared.add(key)
        actual = set()
        for item, body in claim.actual:
            spend()
            if item.scope != desired.source.scope:
                findings.add('unavailable')
                continue  # Never expose the denied identity, count or digest.
            if item in actual:
                findings.add('duplicate-body')
            actual.add(item)
            if item not in desired.dependencies:
                findings.add('undeclared-body')
            if item.version != 1 or item.canonical != 1:
                findings.add('unsupported-version')
                continue
            expected = pin(item.kind, body, scope=item.scope, revision=item.revision,
                           candidate=item.candidate, scenario=item.scenario)
            if expected != item:
                findings.add('integrity-mismatch')
        if set(desired.dependencies) - actual:
            findings.add('missing-materialization')
        spend()
    except (InterruptedError, OverflowError) as error:
        findings.add(str(error))
    # These independent stages cannot be established by an in-memory claim.
    return dict(findings=tuple(sorted(findings)), producer='UNVERIFIED', authorization='UNVERIFIED',
                evidence='INELIGIBLE', satisfaction='NOT_EVALUATED', externalIR='NOT_LOWERED')


@dataclass(frozen=True)
class Node:
    pin: Pin
    adjacency_complete: bool = True


@dataclass(frozen=True)
class Edge:
    identity: str
    kind: str
    source: Pin
    target: Pin


def graph(nodes, edges, *, budget=100, cancelled=lambda: False):
    """Provisional graph rules: dependency cycles reject; logical related cycles stay logical."""
    findings = set()
    def spend():
        nonlocal budget
        if cancelled():
            raise InterruptedError('cancelled')
        budget -= 1
        if budget < 0:
            raise OverflowError('exhausted')
    try:
        indexed = {}; identities = {}; adjacency = {}
        for node in nodes:
            spend()
            key = (node.pin.kind, node.pin.scope, node.pin.revision, node.pin.candidate, node.pin.scenario)
            if key in identities:
                findings.add('duplicate-node' if identities[key] == node.pin else 'conflicting-node')
            identities[key] = node.pin
            indexed[node.pin] = node
            adjacency[node.pin] = []
            if not node.adjacency_complete:
                findings.add('partial-frontier')
            if node.pin.version != 1 or node.pin.canonical != 1:
                findings.add('unsupported-version')
        seen = set()
        for edge in edges:
            spend()
            if edge.identity in seen:
                findings.add('duplicate-or-conflicting-edge')
            seen.add(edge.identity)
            if edge.source not in indexed or edge.target not in indexed:
                findings.add('missing-frontier')
            elif edge.kind == 'dependency':
                adjacency[edge.source].append(edge.target)
            elif edge.kind != 'related':
                findings.add('unsupported-kind')
        # Iterative DFS avoids recursion limits and charges every node/edge visit.
        active = set(); complete = set()
        for root in sorted(indexed):
            stack = [(root, False)]
            while stack:
                spend()
                node, leaving = stack.pop()
                if leaving:
                    active.discard(node); complete.add(node)
                elif node in active:
                    findings.add('dependency-cycle')
                elif node not in complete:
                    active.add(node); stack.append((node, True))
                    stack.extend((target, False) for target in sorted(adjacency[node], reverse=True))
    except (InterruptedError, OverflowError) as error:
        findings.add(str(error))
    return tuple(sorted(findings))
