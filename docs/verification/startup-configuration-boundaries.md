# Startup configuration boundary controls

The focused tests invoke the actual `StartupConfigurationValidator.StartAsync`
with locally constructed options, configuration and real antiforgery registration.
The existing Tenancy Foundation controls and all Product code remain unchanged.

Three valid object-storage profiles are positive controls. Rejections identify
one exact invariant and one error log: production cookie/HTTPS/HSTS/setup and
data-protection requirements, invalid or missing database configuration, weak
or missing storage secrets, development header tenant resolution, malformed
tenant/file/lockout settings, and either missing CSRF middleware or services.
Generated ephemeral values are used only as validator inputs, never for network
authentication; rejection checks retain secret non-disclosure assertions.

These are in-process synthetic configuration controls. The test environment's
Production name does not start a production host, connect to a database, open an
object store, provision keys, change configuration or authorize deployment.
Actual target selection, HTTPS/proxy/cookie/CSRF acceptance, application-role RLS,
Recovery, physical-device acceptance and Owner Release GO remain separate.

Native results, original source/run/attempt/digest bindings, independent review,
current required CI and the original PR Quality Gate must be recorded before
normal expected-HEAD merge. Local coverage is not an additive Main forecast.

The first native build stopped with CS1736 because the new fixture used the
runtime `Environments.Production` field as an optional-parameter constant.
The fixture now defaults a nullable input inside its constructor. The original
failed build log is retained; no tests executed in that first attempt.
