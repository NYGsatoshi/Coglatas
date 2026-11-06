# Dormant Storybook compiler root

The dormant workspace Storybook compiler includes `.storybook/preview.ts` and
source stories but inherited the application's `src` root directory. Its owning
`build-storybook` command failed TS6059 because the preview lies outside that
root. Storybook now explicitly uses its workspace as the compiler root.

This changes only Storybook output-root accounting. Its existing `include`,
`exclude`, `files`, compiler strictness, story selection and Compodoc integration
remain. The application and test compiler configurations, dependencies, active
frontend and deployment are unchanged. The dormant workspace is not promoted.

TypeScript documents that `rootDir` does not change the compilation input set;
all emitted input files must lie beneath it:
https://www.typescriptlang.org/tsconfig/rootDir.html.
The configured Angular Storybook builder and preview/Compodoc integration follow
the existing repository setup:
https://storybook.js.org/docs/get-started/frameworks/angular.

Actual pinned clean-install application/unit/Compodoc/Storybook commands, exact
source and effective input-set comparison must qualify this isolated candidate.
The pre-existing failed builds remain separate records. This is a compiler-root
repair, not a security-finding disposition, an assurance promotion, deployed
Storybook service, browser acceptance or Release GO.
