# Tenant platform profile validation

A combined platform Tenant patch previously staged its name or display name on
the repository-owned object before rejecting a duplicate primary domain. Three
unchanged regression cases reproduce that partial mutation on the original
product. The domain uniqueness check now completes before either name is set.
Authority, errors, normalization, omitted-field behavior, audit and saves remain.
The moved property assignments have ordinary setters and no intervening callback.

The new controls use the existing synthetic EF InMemory seed and real Tenant
repository/authorization services. They cover current persisted administrator
eligibility (including a separately retained stale administrator principal),
creation failures and defaults, normalization, existing-domain reuse,
domain clearing/preservation, missing records, audit identity and unchanged tracked
state on rejection. Every original test body and assertion remains unchanged;
the fixture class is partial so the new cases can reuse it without replacement.

This establishes the Application object-mutation boundary and InMemory persistence
behavior. It does not establish PostgreSQL transactions, deployed RLS, HTTP effects,
real session revocation, concurrent uniqueness enforcement or general atomicity.
In particular, it does not prove that a normal rejected HTTP request later saves
the same context. Existing Tenant authority repairs are retained, not reimplemented.
