# ProjectIDE pre-squash ReSharper evidence

[D13](https://github.com/NYGsatoshi/Coglatas/issues/2197#issuecomment-7167135707)
adds a read-only procedural approval step for an independently authorized
ProjectIDE squash. The six tracked contexts and their GitHub Actions producer
remain required by this procedure. Repository enforcement and review authority
remain with GitHub and the authorized integration owner.

The recorded local candidate `7195a13fb4c6f3e13735435218a8354f158cb6d1` was
unavailable in the execution checkout and GitHub returned no commit for that
SHA. This reconstruction uses the source and control contract from
[#1971](https://github.com/NYGsatoshi/Coglatas/pull/1971), the current canonical
PR CI and [the #1954 acceptance record](https://github.com/NYGsatoshi/Coglatas/issues/1954).
The original #1946 sequencing discrepancy remains historical evidence.

## Before an authorized squash

1. Qualify and review the current source through the applicable CI and review
   procedure. Record the independently verified whole tree of the actual GitHub
   test merge. Identify the successful ordinary PR CI run and its current
   attempt.
2. Mark the PR Ready when its applicable conditions pass. From the reviewed
   evaluator checkout, execute this read-only check immediately before the
   authorized expected-head squash.

   ```sh
   python3 -B scripts/ci/projectide_resharper_integration_gate.py \
     --repository NYGsatoshi/Coglatas --pr "$pr" \
     --expected-head "$head" --expected-base "$base" --expected-tree "$tree" \
     --run-id "$run" --attempt "$attempt" > "$receipt"
   ```

   If GitHub's CLI cannot download the archive, obtain the original provider
   ZIP through the connected GitHub artifact download and pass
   `--artifact-zip "$archive"`. The evaluator verifies the same live provider
   metadata, upload interval, archive digest and SARIF source in either case.
3. Require a successful exit and retain the JSON receipt before issuing the
   expected-head squash. Stop on HOLD. Requalify after any change in source,
   base, test merge, attempt or review disposition. Compare the landed sole
   parent and tree with the receipt. This procedural check and GitHub's squash
   are separate requests, so a base move between the last check and the squash
   remains possible. Serialize target writes and verify the actual parent.

The receipt binds the repository, PR, head, live base, ordered test merge and
whole tree, workflow, attempt, InspectCode check, artifact digest and resolved
thread count. The evaluator checks terminal ordinary CI, the six tracked
contexts, applicable required status checks and ordered InspectCode steps. It
reads the original report's source revision and verifies the integrated Entry
probe receipt when applicable. Raw SARIF, GitHub responses and authentication
data are excluded from output. A qualified receipt supplies technical CI and
review-thread evidence. It does not supply missing authorization or formal
source review.

Run the controls locally with the same Python interpreter used for the gate.

```sh
python3 -B -m unittest discover -s tests/ci -p 'test_projectide_*resharper*.py' -v
```
