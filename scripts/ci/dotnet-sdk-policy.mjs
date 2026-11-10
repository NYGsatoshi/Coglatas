// The repository's release SDK policy has two exact, audited choices:
// stable .NET 10 for production and GA .NET 11 only for the isolated migration PR.
// Keep this pure so negative controls can exercise it without Docker or tokens.
export function resolveDotnetSdkPolicy(policy) {
  const version = policy?.version;
  const stable = typeof version === 'string' &&
    /^10\.0\.\d{3}$/.test(version) &&
    policy.rollForward === 'disable' &&
    policy.allowPrerelease === false;
  const ga11 = version === '11.0.100' &&
    policy.rollForward === 'disable' &&
    policy.allowPrerelease === false;
  return {
    approved: stable || ga11,
    sdkImageTag: version,
    runtimeTagPattern: ga11 ? '11\\.0\\.0' : '10\\.0\\.\\d+',
  };
}
