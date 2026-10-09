# Pinned isolated Kafka launcher qualification

The fixture retains its pinned Kafka 4.1.2 image, isolated network, non-root
owner-only client files, exact ACL inventory, 16 broker controls and existing
command deadlines. Kafka remains inactive in the product.

On the current local environment, the pinned shell TopicCommand help startup
probe took 14.21 seconds, exceeding the authentication probe's 13-second inner
deadline. Direct Java launch of the same class took 6.59 seconds including
disposable-container startup. These are local diagnostic observations, not
performance gate measurements or a change to any threshold.

The fixture now invokes the exact entry-point classes from the
[pinned upstream tool scripts](https://github.com/apache/kafka/tree/4.1.2/bin)
using the image's JAR classpath and tool logging configuration. The closed class
map preserves the legacy `kafka.admin.ConfigCommand` entry point used in 4.1.2.
Arguments remain separate process arguments. Positive Alpha/Beta production and
consumption precede the invalid-credential write through the same bounded client.
The authentication deadline is unchanged. A timeout, missing exception or
unrelated permission/error mechanism cannot qualify a denial.

Version 3 receipts distinguish outer-process and inner-container timeouts and
retain only fixed exception-class observations and exit/status fields. They do
not export command outputs, configurations, credentials or synthetic messages.
The environment fingerprint identifies the direct pinned-tool launch. Candidate
and ownership checks, exclusive output and owned cleanup remain enforced.

## Retained development evidence

The earlier clean-candidate authentication failure and all development failures
remain unchanged. The development reports below belong to an uncommitted source
revision based on `8c1bd3c1207d2c5a7b59387a72226eba4389ceb8`; none is a committed
candidate attestation.

| Report | Result | SHA256 |
| --- | --- | --- |
| Development v1 | Authentication inner timeout; ERROR | `03c4d58c9f641fcb0ddc2474fef0e14d14131add776db19486dcad6d0646d5c4` |
| Development v2 | Bounded producer through shell still timed out; ERROR | `d5a2d499a9404fbf305a2efac8165d88efdc76d8760161e25a7a75972968998c` |
| Development v3 | Authentication passed; incorrect configuration-tool entry point failed; ERROR | `a92b6e1dc85269666a825bb40d7eeb8ad830f998013b1691c8317a339dbd32fe` |
| Development v4 | All 16 actual broker controls passed; dirty-source outcome UNVERIFIED | `4e5a0b12ce61781466aec6d73e9bc9aee8271f8f62abc394c1b49eec0e7679a1` |

All 16 launcher/ACL mutation unit controls also passed. Exact clean integration
candidate and trusted CI evidence remain required. Synthetic broker success does
not establish approved SPEC linkage, product consumer/Outbox identity, production
Kafka activation, cloud network enforcement or full pre-Avalonia acceptance.
