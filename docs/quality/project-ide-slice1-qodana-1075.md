# Project IDE Slice 1 Qodana dispositions — #1075

Exact Main: `3a453597a83434e549668c5b7095f58d0c9f7cd8`. Main CI `37267873315/1`, artifact `11328156275` (`qodana-full-inventory`), verified ZIP SHA-256 `b583c3aee5886cb89161dd9eae201cc53fb5e4e248078cf319a28405875f28a4`.

All 43 findings responsible for the six ratchet increases are classified individually below. The existing `SimplifyLinqExpressionUseAll` suggestion is outside those increases. No public member is deleted or privatized. No suppression, baseline, scanner profile, codec/persistence format, threshold or new IDE slice is introduced.

Four results are FIX_IMPLEMENTATION: a cloned JsonElement in a get-only auto-property, explicit Guid.Empty, output-only generic variance, and a private same-class BranchData helper. The other 39 are INTENTIONAL_PUBLIC_CONTRACT. Tests assert UUID factory uniqueness/version/variant, construction, parsing, invalid/default output and JSON round-trip for all eleven types. Codec tests assert scope, manifest order, document names, typed Relation locations, format and redacted status-specific failure reasons. Each assertion can fail on a contract regression; reference-only calls are not added.

Local Release verification: Core/codec 61/61 PASS; Project IDE architecture boundaries 3/3 PASS; no skips. Hosted candidate checks and exact post-merge Main Community/Cloud acceptance are still required. Do not close #1075 on this local result.

| # | Main location | Inspection / member | Disposition | Evidence / repair |
| --- | --- | --- | --- | --- |
| 1 | `CanonicalJson.cs:23` | `ConvertToAutoProperty`: Convert into auto-property | `FIX_IMPLEMENTATION` | Frozen cloned JSON value; getter signature preserved. |
| 2 | `ProjectLocation.cs:20` | `MemberCanBePrivate.Global`: Constant 'Format' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectLocationTests: exact format and typed Relation round-trip. |
| 3 | `ProjectIdentities.cs:121` | `MemberCanBePrivate.Global`: Constructor 'BranchId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 4 | `ProjectIdentities.cs:169` | `MemberCanBePrivate.Global`: Constructor 'CandidateRevisionId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 5 | `ProjectIdentities.cs:217` | `MemberCanBePrivate.Global`: Constructor 'DocumentId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 6 | `ProjectIdentities.cs:89` | `MemberCanBePrivate.Global`: Constructor 'EntityId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 7 | `ProjectIdentities.cs:73` | `MemberCanBePrivate.Global`: Constructor 'ProjectId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 8 | `ProjectIdentities.cs:153` | `MemberCanBePrivate.Global`: Constructor 'ProposalId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 9 | `ProjectIdentities.cs:105` | `MemberCanBePrivate.Global`: Constructor 'RelationId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 10 | `ProjectIdentities.cs:137` | `MemberCanBePrivate.Global`: Constructor 'RevisionId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 11 | `ProjectIdentities.cs:185` | `MemberCanBePrivate.Global`: Constructor 'ScenarioId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 12 | `ProjectIdentities.cs:201` | `MemberCanBePrivate.Global`: Constructor 'ScenarioRevisionId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 13 | `ProjectIdentities.cs:57` | `MemberCanBePrivate.Global`: Constructor 'TenantId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 14 | `SourceContext.cs:157` | `MemberCanBePrivate.Global`: Method 'BranchData' can be made private | `FIX_IMPLEMENTATION` | BranchData has only same-class callers; no public contract. |
| 15 | `ProjectSource.cs:107` | `MemberCanBePrivate.Global`: Property 'ProjectId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectSourceTests: scope/manifest/name consistency or redacted status-specific decode reason. |
| 16 | `ProjectSourceCodec.cs:13` | `MemberCanBePrivate.Global`: Property 'Reason' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectSourceTests: scope/manifest/name consistency or redacted status-specific decode reason. |
| 17 | `ProjectSource.cs:106` | `MemberCanBePrivate.Global`: Property 'TenantId' can be made private | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectSourceTests: scope/manifest/name consistency or redacted status-specific decode reason. |
| 18 | `ProjectIdentities.cs:31` | `PreferConcreteValueOverDefault`: Use 'Guid.Empty' instead of 'default' | `FIX_IMPLEMENTATION` | Explicit Guid.Empty; failure-output semantics unchanged. |
| 19 | `ProjectIdentities.cs:6` | `TypeParameterCanBeVariant`: The type parameter 'TSelf' could be declared as covariant | `FIX_IMPLEMENTATION` | Output-only TSelf; constraints and identity members preserved. |
| 20 | `ProjectSource.cs:70` | `UnusedAutoPropertyAccessor.Global`: Auto-property accessor 'DocumentIds.get' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectSourceTests: scope/manifest/name consistency or redacted status-specific decode reason. |
| 21 | `ProjectSource.cs:29` | `UnusedAutoPropertyAccessor.Global`: Auto-property accessor 'LogicalName.get' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectSourceTests: scope/manifest/name consistency or redacted status-specific decode reason. |
| 22 | `ProjectLocation.cs:23` | `UnusedAutoPropertyAccessor.Global`: Auto-property accessor 'RelationId.get' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectLocationTests: exact format and typed Relation round-trip. |
| 23 | `ProjectIdentities.cs:58` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 24 | `ProjectIdentities.cs:74` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 25 | `ProjectIdentities.cs:90` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 26 | `ProjectIdentities.cs:106` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 27 | `ProjectIdentities.cs:122` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 28 | `ProjectIdentities.cs:138` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 29 | `ProjectIdentities.cs:154` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 30 | `ProjectIdentities.cs:170` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 31 | `ProjectIdentities.cs:186` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 32 | `ProjectIdentities.cs:202` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 33 | `ProjectIdentities.cs:218` | `UnusedMember.Global`: Method 'New' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 34 | `ProjectIdentities.cs:60` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 35 | `ProjectIdentities.cs:76` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 36 | `ProjectIdentities.cs:108` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 37 | `ProjectIdentities.cs:124` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 38 | `ProjectIdentities.cs:140` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 39 | `ProjectIdentities.cs:156` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 40 | `ProjectIdentities.cs:172` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 41 | `ProjectIdentities.cs:188` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 42 | `ProjectIdentities.cs:204` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |
| 43 | `ProjectIdentities.cs:220` | `UnusedMember.Global`: Method 'TryParse' is never used | `INTENTIONAL_PUBLIC_CONTRACT` | ProjectIdentityBoundaryTests: factory, constructor, parsing and invalid/default/JSON controls. |

Classification totals: FIX_IMPLEMENTATION 4; INTENTIONAL_PUBLIC_CONTRACT 39; PLANNED_USAGE_REQUIRED 0; FALSE_POSITIVE 0; CONTRACT_CHANGE_REQUIRED 0.

## Exact result identities

| # | Main SARIF equalIndicator/v1 |
| --- | --- |
| 1 | `677AD3B638AAC91D640E3C6850F4F271081A144544EE7C8A8F6A963A558AC432` |
| 2 | `003C75700C433547B496AA3A76AFCC8793135294216F4112547F0AB191F329D9` |
| 3 | `9357D0E8483C524EA14010F795265CE0E13ED59A0A8F7F9A5EBCD0A4C3FBA944` |
| 4 | `0A207BCA87E83DBD212648C853579437C812F9FC0FA7D68E419C26D6FFA5B73C` |
| 5 | `515CAAE37DAE4A93EF68D2358968529D42CB590BAF34B0017BE2B5EAD9DD9AF8` |
| 6 | `F375DEC931165C79331FC62741DC5B50158FCAE645A026594333FDFA5DEAFE6D` |
| 7 | `AF44C64A2072CC47914AFBD18BDCFDCCCCE8494BE3DCCD3B40E86196ADF4C407` |
| 8 | `739B2AFDE9E62EE40FE2907923F5D16E75FE9A50945DF9BF3FB17697638B28A4` |
| 9 | `5CBB9D6376DD7738D75E0DCC2C3E27FEF95C8119D0AA792066E1803F1DF62192` |
| 10 | `E4263F2B75A073EA39DD4A88C841CADC2427CE5516E2902537072178DDE686BB` |
| 11 | `7B1AFF0CB3FCF522532BB484233804747F75F294065F967C50B6FE19831C062B` |
| 12 | `95FADB5FDCB19DC7B27E846E901D9522A50C05050E8E1008D0FAF3312AC17FC1` |
| 13 | `FFAA42A8EB2D62ADAEF5A961BAFF40B30B9A6FE54F8709776564BD5C67ADE097` |
| 14 | `88C1DA36F2A12B7424F74D465FC3023D1B6C357ACF8B3B74BDF9EED19B91816D` |
| 15 | `3F0EBD8441DD12EB7FA72599F4DAD54AA336F9CA7D2D718A5BCBC8C85D7B5D50` |
| 16 | `98BA7AAD5D2C23A9080032E22C4EE0201E1765A84B240788544BFC266D890DA5` |
| 17 | `035B7ED786AF277732D83E838284449A4FFF6A674070AA51CE4A15F5267A89E9` |
| 18 | `BA4E6CE46FCC1B7F227E27B487263F8F6FFD9542D066C98915653CE4CF1FE566` |
| 19 | `F28470528D05B841672F746E060084AD6CAD13EBC1F1E549589C38F5213A798A` |
| 20 | `6B55EABFB24DAE2836C85A72FDD7B1A6609D2FDC1B9A704FEE038F6BC0DB93BA` |
| 21 | `58F09CF00A31CFAEAE05021AA8BA310601DBF9A5281361698FD67E6C901BD662` |
| 22 | `2EE3E8343D93DC8EFF90F0E0F70A84519799A9E937A67349A3D66192CAF90382` |
| 23 | `7B69FAED671B79C6A4C981135F3B024634AA9D4A20DEE3C34316C464C0221345` |
| 24 | `F9573DF7BCEED4D9904DFF3E45E5F23EE77D7D26BDEE6DD2A8E5B7D0B9E892D7` |
| 25 | `F2200AD4AC11705A0D148FDE4682C58A40ADEBFB62344C9D459996D046E1153C` |
| 26 | `7EEEC137DC34B97963A5BA781BFB53C8469A8A05C1D882A46F724E24B5A27072` |
| 27 | `98F882E137606CCF7B5D05E0B76A4568A7302D0F1CE8E73DCE46A1D8B8947F25` |
| 28 | `15E952572FD1026C0C0894A65514BC3E716A478C7BB538FFDE1C9351DC41624A` |
| 29 | `E6BEAA04BA853B43BCBAE52D5ACA4154D781E4556BCA45A7AC38F013C0611BF4` |
| 30 | `DC9B41CD27B74B07F2D5FF8193F20F2453833DC4CD21CB86B5C2CFD4E4181339` |
| 31 | `FFF2E3A31A6575DB96E2B71F50FBE1D48F5CC89F6A2AAFC1B6BDAC954CE0C500` |
| 32 | `C73A7C1DED5364F8A33ABFB7DC1B332B5CA9B5DFD311F14154CF008A7D14DBB5` |
| 33 | `3F5C55E8345DF9A5E783D32DECB1B8DA1387BC55984F3D5C4E4B77C57139F32F` |
| 34 | `499F99CD6C3E4FB6EA80B6057B02A131E91AC4D06507DFCC8D05E2E76D6AFFBF` |
| 35 | `3B4C4BAFDC8BDDDCF875153389A9084F7BA0102A7238C99DAE6BA7B54E062A5F` |
| 36 | `23711311A146E19CC4E9949952983121029F7A8625BA11302ED077325187CCDD` |
| 37 | `3BBC77BC91456C0808B34409802596CD15C03B3B22B9161C67F5B6E8661DF895` |
| 38 | `F7D1DA56A18F2BAF0BC37EFEAD077675EF439A78EC9FFA08E938BD7C2EE5B67F` |
| 39 | `C89FE2BC62CC278AF3577091E0B755C1A1C489967B657D4B804015373120DF96` |
| 40 | `0DCE3230C5AD5F26A26C2354C1C1ACD43EA3C5BBB84E25A1D4BB3E76AFD7AAB3` |
| 41 | `C90C3534CA27455E5C9617FB1F537BD83E01677680F36FEBCA635583255572EE` |
| 42 | `D103488653D5CA60065806DD39DCA07249D5A7C60744CE85BDB40EF043640816` |
| 43 | `38BBE5573D0E9C615804AA3431729988B3A51E8EDFD7B3C03FF222CC48AF03D4` |
