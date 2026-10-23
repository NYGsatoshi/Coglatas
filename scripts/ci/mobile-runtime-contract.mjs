/** Finite source/image binding; no container configuration or credentials escape. */
export function mobileRuntimeIdentity({ sha, stamp, checkout, dirty, image, running, reused, producerSource, producerId }) {
  if (!/^[0-9a-f]{40}$/.test(sha ?? '') || stamp !== sha || checkout !== sha || dirty ||
      !/^sha256:[0-9a-f]{64}$/.test(image?.Id ?? '') || running?.Image !== image.Id || running?.State?.Running !== true ||
      (reused ? producerSource !== sha || image.Id !== producerId : image.Config?.Labels?.['org.opencontainers.image.revision'] !== sha)) {
    throw new Error('Mobile source or producer image binding failed.');
  }
  return { schemaVersion: 1, commitSha: sha, imageId: image.Id,
    producer: reused ? 'restored-main-artifact' : 'fresh-isolated-build', containerMatchesProducer: true };
}
