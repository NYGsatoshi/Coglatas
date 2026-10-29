# Lone-surrogate AST compatibility

This candidate changes two existing invalid member-search test strings from
escaped surrogate literals to runtime construction. The complete eight-input
matrix has identical UTF-16 bytes (112 code units), including both unpaired
surrogates and both NUL cases. Every other normalized source byte, the negative
HTTP assertion and application implementation remain unchanged. No exclusion,
suppression, rule, threshold, retry, timeout or authorization contract changes.

## Observed analyzer limitation

Main f5735c030342307fc161d2362cc598edab9e6286 native analysis40802199172/1,
job123212702727, logged invalid-UTF-8 Protobuf at02:02:43.606Z with
SonarJS14.3.0.46350. Execution succeeded; official Gate123218781712 failed
36.1%NewCode/ReliabilityD/SecurityE. This is not complete Taint assurance.
Mainf14e46be6769559b724a6bd6d294e21a08cdadc1 retains the same fixture.

[SonarSource's JS-2753 reproduction and explanation](https://community.sonarsource.com/t/sonarjs-14-3-invalid-utf-8-protobuf-errors-pr-analysis-times-out-before-upload/190532/3)
confirms that valid source with a lone-surrogate literal can trigger the codec
failure and omit Taint AST consumption. The scanner can still finish.
The pinned [AstProtoUtils implementation](https://github.com/SonarSource/SonarJS/blob/822582ba4369505b8701de3c4a0029e7f769e93a/sonar-plugin/bridge/src/main/java/org/sonar/plugins/javascript/bridge/AstProtoUtils.java)
returns null after this error; [WebSensor](https://github.com/SonarSource/SonarJS/blob/822582ba4369505b8701de3c4a0029e7f769e93a/sonar-plugin/sonar-javascript-plugin/src/main/java/org/sonar/plugins/javascript/analysis/WebSensor.java)
only invokes AST consumers for a non-null result. Neither source establishes
the exact file responsible for this repository's historical failure.

## Verification and limits

A local Node equivalence check evaluates the original and updated fixture
expressions, compares each complete UTF-16 byte sequence, independently checks
surrogate/NUL positions and reconstructs all other normalized source bytes.
All checks pass. This is representation equivalence, not local Angular or full
analyzer execution. The local machine lacks frontend dependencies and has only
about330MiB free; existing full hosted validation remains required.

Fresh exact-head PR CI,1919existing Angular cases, independent review and native
Sonar analysis must be checked. A resulting Main requires its own first-attempt
qualification and complete analysis observation; absence of this one warning
does not prove complete Taint analysis, SecurityA,80%coverage or release approval.
The Announcement alias repair, shell-parser warnings and all formal/FCI06/Owner
holds remain separate. STUDENT_COUNCIL_PILOT = RELEASE_NO_GO.
