import assert from 'node:assert/strict';
import test from 'node:test';
import { resolveDotnetSdkPolicy } from './dotnet-sdk-policy.mjs';

test('stable .NET 10 pins remain exact and non-prerelease', () => {
  for (const version of ['10.0.403', '10.0.999']) {
    const result = resolveDotnetSdkPolicy({
      version, rollForward: 'disable', allowPrerelease: false,
    });
    assert.equal(result.approved, true);
    assert.equal(result.sdkImageTag, version);
    const runtime = new RegExp('^' + result.runtimeTagPattern + '$');
    assert.match('10.0.14', runtime);
    assert.doesNotMatch('11.0.0', runtime);
  }
});

test('exact .NET 11 GA pin maps to official image tags and rejects prereleases', () => {
  const result = resolveDotnetSdkPolicy({
    version: '11.0.100',
    rollForward: 'disable',
    allowPrerelease: false,
  });
  assert.equal(result.approved, true);
  assert.equal(result.sdkImageTag, '11.0.100');
  const runtime = new RegExp('^' + result.runtimeTagPattern + '$');
  assert.match('11.0.0', runtime);
  assert.doesNotMatch('11.0.0-rc.3', runtime);
  assert.doesNotMatch('11x0x0', runtime);
  assert.doesNotMatch('10.0.14', runtime);
});

test('unapproved SDKs, malformed policies and roll-forward fail closed', () => {
  const examples = [
    null, undefined, {}, { version: 11 }, { version: '11.0.100' },
    { version: '10.0.403', rollForward: 'disable', allowPrerelease: true },
    { version: '10.0.403', rollForward: 'latestPatch', allowPrerelease: false },
    { version: '10.0.403', rollForward: 'disable' },
    { version: '11.0.100-rc.2.12345.678', rollForward: 'disable', allowPrerelease: true },
    { version: '11.0.100-rc.3.26522.148', rollForward: 'disable', allowPrerelease: true },
    { version: '11.0.100-rc.3.26522.148x', rollForward: 'disable', allowPrerelease: true },
    { version: '11.0.100', rollForward: 'disable', allowPrerelease: true },
    { version: '11.0.100', rollForward: 'latestPatch', allowPrerelease: false },
  ];
  for (const value of examples) {
    assert.equal(resolveDotnetSdkPolicy(value).approved, false, JSON.stringify(value));
  }
});
