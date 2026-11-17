# Existing database and performance control coverage

The canonical Sonar Python producer now executes eight existing offline suites that were absent from its collection loop. All original 56 entries retain their relative order and commands. The eight new entries use exact-file `unittest discover`, including suites without a `__main__` entry point. The original finite mutation CLI remains last.

| Existing suite | Cases |
| --- | ---: |
| `test_performance_db.py` | 16 |
| `test_performance_db_candidate.py` | 8 |
| `test_performance_db_assignment_transition.py` | 39 |
| `test_performance_db_approval_history.py` | 4 |
| `test_performance_environment_class.py` | 9 |
| `test_performance_public_digest.py` | 3 |
| `test_performance_runtime_compatibility.py` | 10 |
| `test_performance_diagnostic_recovery.py` | 10 |

Two additive producer controls compare all declared and discovered test identities, reject duplicate selection or loader hooks, and execute the actual selected discovery command against 99 synthetic cases without `__main__`. Every synthetic child writes its own native branch-coverage shard containing only the intended subject. The original 14 producer controls retain their assertions; their exact source-set assertion adds only the five new operational subjects. All eight existing suite bodies and assertions are byte-identical.

The source include list retains all original 76 subjects in order and appends five operational Python files directly exercised by these suites: `db-compare.py`, `db_class_baselines.py`, `collect-environment.py`, `diagnostic_recovery.py`, and `announcement-diagnostic.py`. Test bodies remain outside coverage. Collection still uses the single pinned native producer, process bootstrap, original raw shards, combined native data and one XML import. Source identity, line/branch accounting, missing ranges and zero-hit ranges must be qualified from actual output. No report bytes, exclusions, thresholds or quality budgets are rewritten.

Local Windows diagnostics executed all 99 existing cases. The initial diagnostic recovery run reported three fixed-digest assertion failures caused by CRLF expansion of checkout bytes. Its original log is retained. Raw Git bytes match the unchanged versioned rule; a source-snapshot diagnostic using those original bytes passes all 10 cases. The prior working-tree bytes were restored afterward. This does not change any Git blob, test assertion, collector digest or recovery approval rule.

The suites use synthetic inputs, repository doubles, mocked subprocesses and isolated temporary Git repositories. They do not run production database migrations, benchmarks, real browser preparation, external recovery or approved performance campaigns. Numerical performance assurance remains suspended and not evaluated. No Source 82, baseline, environment mapping, required-check promotion, Owner HOLD, security disposition or Release GO is activated.

Supported hosted execution, all 64 canonical entries, all 81 subjects, original report/source mapping, raw bytes, producer run/attempt/artifact digests, official PR Quality Gate and independent review are required before integration. After integration, measure the resulting Main independently. Local coverage increments must not be added together or presented as a Main coverage improvement.
