# Optimized task query pagination overflow

`ProjectRepository.ListTasksPageAsync` now computes its normalized offset in
64-bit arithmetic and bounds the argument to `Skip` at `int.MaxValue`, matching
the existing project-list query. Parent, priority, deletion, ordering, projection,
count, cancellation and page-size behavior remain unchanged. The search fallback
and shared in-memory child paginator are separate paths.

The test-only source `4fc28fc4518a199cec9b7ede8ce992f897b3de74` produced four
oversized-offset assertion failures and seven passing ordinary/normalization
controls on Windows EF Core InMemory. Its additional PostgreSQL case failed
before opening a connection because `POSTGRES_TEST_CONNECTION_STRING` was
unavailable. That environment failure is retained and is not SQL execution proof.
The original results and coverage bytes are preserved outside the repository.

The new PostgreSQL control runs all eleven variations in migrated temporary
databases with transaction rollback. A passing current hosted execution is still
required; InMemory success alone does not qualify SQL translation. Existing test
methods and assertions are unchanged. These controls do not establish caller
authorization, HTTP behavior, RLS, concurrency, physical acceptance or the cause
of historical messaging failures.
