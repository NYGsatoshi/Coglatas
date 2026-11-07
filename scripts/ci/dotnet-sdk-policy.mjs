// The repository's release SDK policy has two exact, audited choices:
// stable .NET 10 for production and RC3 only for the isolated migration PR.
// Keep this pure so negative controls can exercise it without Docker or tokens.
export function resolveDotnetSdkPolicy(policy) {
  const version = policy?.version;
  const stable = typeof version === 'string' &&
    /^10\.0\.\d{3}$/.test(version) &&
    policy.rollForward === 'disable' &&
    policy.allowPrerelease === false;
  const rc3 = version === '11.0.100-rc.3.26522.148' &&
    policy.rollForward === 'disable' &&
    policy.allowPrerelease === true;
  return {
    approved: stable || rc3,
    sdkImageTag: rc3 ? '11.0.100-rc.3' : version,
    runtimeTagPattern: rc3 ? '11\\.0\\.0-rc\\.3' : '10\\.0\\.\\d+',
  };
}
