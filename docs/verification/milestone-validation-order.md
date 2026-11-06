# Milestone update validation order

The compatibility `ProjectService.UpdateMilestoneAsync` previously assigned the
trimmed title before validating the requested status and the final date/status
combination. A request could fail while leaving the tracked Milestone name
changed. The assignment now occurs after these existing input checks, alongside
the other assignments. Authorization, optimistic-version checks, accepted
statuses, errors, audit publication, invalidation and save behavior are retained.

The first three regression cases at commit `170d97a4` ran against the unchanged
Main `d36c2d8e33f93f094fd937f7f50861e937d568ef` product. All three failed at the
unchanged-title assertion: the request was rejected with the expected error,
but the observed name was `Rejected title` rather than `Original title`.
Original log SHA256:
`3ca3abfefb369bae83ee4e0378b39741c48beadbdc5180112d40d0665c19b4d1`;
original TRX:
`37313f28e9a376c7471f6fc5752191e7c3e506cca9261c7a3f14e0776e733464`.
These failures are retained separately from subsequent fixed-source results.

The additional controls cover existing undated active/completed state with an
omitted status, and successful updates for all three accepted statuses with
either replacement title/date or retention of the existing values. They check
returned and tracked values, version, audit identity, invalidation and save
counts. Existing Project test bodies and assertions remain unchanged; the
original class is made partial to share its established fixture.

At fixed-source commit `d0c936ff1ebc6e2c76fa24d5e06493944472d431`, the owning
ProjectService cohort passed 119/119 with no failure or skip: 108 existing cases
and 11 new cases. Original log SHA256:
`0ac41016467705de803085164f1378b77c49afe1f95c1682729ef6890443f34c`;
TRX `c20ab4b1c1d4d3014ba84c8b6d7b110b4aa15645315eef5393e76e6af6a9c0b2`;
OpenCover `d79732f442f47939950dc1cce88b6668f6964c706422b7ab7874b3a2e2c3fd2a`
with one identical VSTest copy. Source verification confirms that the only
product difference is the existing assignment relocation and that all existing
tests and the three initial regression bodies are retained. A native local
InspectCode2026.2.2 no-build/no-SWEA suggestion preflight for the new test file
completed successfully with zero findings. This scoped Debug-model diagnostic
is not hosted Full Qodana qualification. This evidence paragraph changes no
product or test source after those runs.

Run the owning Project service cohort with:

```powershell
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj -c Release --filter FullyQualifiedName~Coglatas.Tests.Projects.ProjectServiceTests --collect "XPlat Code Coverage;Format=opencover"
```

These are Application tests using the existing repository/effect doubles. They
do not prove PostgreSQL transaction behavior, HTTP atomicity, RLS enforcement,
or general rollback on save/audit/invalidation failure. No production operation,
formal security disposition or Owner promotion is involved. Resulting Main CI,
Full Qodana and original official Sonar remain separately required.
