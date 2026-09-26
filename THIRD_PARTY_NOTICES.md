# Third-party notices

Coglatas depends on third-party packages and tools. Those components are not
licensed under the repository-owned source terms in
[COPYRIGHT.md](COPYRIGHT.md). Their authors and licensors retain their rights,
and their own license terms continue to apply.

## Syncfusion

The frontend references Syncfusion Essential Studio packages, including
Syncfusion Angular components. Syncfusion software is licensed separately.

- This repository does not grant a Syncfusion license.
- A Syncfusion license key must not be committed, embedded in browser assets,
  printed to logs, or uploaded as a CI artifact.
- Anyone who builds, develops, deploys, or uses Syncfusion-dependent
  functionality is responsible for obtaining and complying with an appropriate
  Syncfusion license.
- Licensed CI jobs use the protected `syncfusion-licensed-build` GitHub
  Environment. The license value must exist only as an environment secret named
  `SYNCFUSION_LICENSE`.

Package manifests and lock files identify other npm and NuGet dependencies.
This file is a notice, not a complete reproduction of every dependency license.
Distributors remain responsible for reviewing and satisfying all applicable
third-party terms.
