# SDK image digest coordination

The Main SDK digest update left four Compose references and the synthetic
logical-recovery runner/receipt validator on a different image digest with the
same10.0.403 tag. Registry manifest bytes independently hash to each declared
digest. Their linux/amd64 child manifests differ, so tag parity does not prove
image-byte identity or qualification of the other path.

All live consumers now use the existing root Dockerfile SDK digest
212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e.
The recovery positive fixture is synchronized with the runner and receipt
validator. The required publication-readiness parity guard also rejects
same-version digest drift, with negative mutations in a Compose carrier and
the recovery validator. Existing version, immutable-pin, Node, Vite and
dependency-approval assertions remain in place.

The original guard passed35 negative controls despite the mismatched pins.
The strengthened test-only guard then rejected the six actual stale carriers.
Those original records remain retained. A passing static guard or unit suite
does not establish a working migration, application, synthetic recovery or
deployed target; current candidate runtime qualification remains separate.

No Product authorization, RLS, EF migration, data schema, runtime acceptance,
quality threshold, exclusion, finding disposition or release decision changes.
No historical report or recovery receipt is rewritten. Current synthetic
execution evidence does not approve production backup/restore or Pilot GO.
