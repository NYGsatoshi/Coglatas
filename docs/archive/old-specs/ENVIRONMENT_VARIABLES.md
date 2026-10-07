# Environment Variables

Use double underscores to override nested configuration keys.

## Required Runtime

- `ASPNETCORE_ENVIRONMENT`
- `ConnectionStrings__DefaultConnection`
- `DataProtection__KeysPath` for production deployments

## Tenancy

- `Tenancy__AppMode`
- `Tenancy__DefaultTenantSlug`
- `Tenancy__TenantResolutionStrategy`
- `Tenancy__AllowTenantSwitching`
- `Tenancy__AllowDevelopmentHeaderTenantResolution`
- `Tenancy__AllowDevelopmentHeaderInProduction`
- `Tenancy__DevelopmentTenantHeaderName`
- `Tenancy__TenantCookieName`

## File Storage

- `FileStorage__Provider`
- `FileStorage__RootPath`
- `FileStorage__BucketName`
- `FileStorage__Endpoint`
- `FileStorage__Region`
- `FileStorage__MaxFileSizeBytes`
- `FileStorage__AllowedExtensions__0`
- `FileStorage__AllowedExtensions__1`
- `FileStorage__AllowedContentTypes__0`
- `FileStorage__AllowedContentTypes__1`
- `FileStorage__UseSignedUrls`
- `FileStorage__UsePathStyle`

Do not commit object storage access keys or database passwords. Provide them through a protected environment, secret manager, or deployment platform secret store.

## Security And Platform

- `Security__CookieSecurePolicy`
- `Security__RequireHttps`
- `Security__EnableHsts`
- `Security__EnableCsrfProtection`
- `Security__EnableRateLimiting`
- `Security__LoginLockoutEnabled`
- `Security__MaxFailedLoginAttempts`
- `Security__LoginLockoutDurationMinutes`
- `Platform__EnablePlatformAdmin`
- `Platform__PlatformAdminSetupMode`
- `Platform__AllowTenantCreationFromAdmin`
- `Platform__EnablePlansAndSubscriptions`
- `Platform__EnableUsageQuota`

## Docker Compose

- `POSTGRES_DB`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `POSTGRES_PORT`
- `COGLATAS_PORT`
- `FILE_STORAGE_MAX_FILE_SIZE_BYTES`
