import {Digest, PreviewCase, ProjectLocation, sameDigest} from './projectide.contracts';
import {AdmittedRootSource} from './projectide-root-source.reader';
import {AdmittedRecordedPassBoundary, recordedPassBoundaryMatches} from './projectide-pass-boundary.reader';
import {resolveLocation} from './projectide-preview.adapter';
import {isProjectLocationShape} from './projectide-preview.validation';

export interface PassBoundarySourceTarget {
  readonly kind: 'node' | 'relation';
  readonly identity: string;
  readonly location: ProjectLocation;
}
export interface PassBoundarySourcePage {
  readonly offset: number;
  readonly maximumTargets: number;
  readonly totalTargets: number;
  readonly originalMapDigest: Digest;
  readonly originalRecordDigest: Digest;
  readonly targets: readonly PassBoundarySourceTarget[];
}
interface PageBinding { readonly value: PreviewCase; readonly sources: AdmittedRootSource; readonly boundary: AdmittedRecordedPassBoundary; }
const bindings = new WeakMap<PassBoundarySourcePage, PageBinding>();
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v,i) => equal(v,b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b,k) && equal(a[k],b[k]));
const freeze = <T>(value: T): T => { if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); } return value; };
const fail = (): never => { throw new Error('unsupported-pass-source-page'); };

export function passBoundarySourcePageMatches(value: PreviewCase, sources: AdmittedRootSource,
  boundary: AdmittedRecordedPassBoundary, page: PassBoundarySourcePage): boolean {
  const binding = bindings.get(page);
  return !!binding && binding.value === value && binding.sources === sources && binding.boundary === boundary &&
    recordedPassBoundaryMatches(value, sources, boundary.recording, boundary);
}

/** Derive only a bounded original-order page from an explicitly read retained map; never borrow the current result's map body. */
export function readPassBoundarySourcePage(value: PreviewCase, sources: AdmittedRootSource,
  boundary: AdmittedRecordedPassBoundary, offset = 0, maximumTargets = 32): PassBoundarySourcePage | null {
  if (!Number.isSafeInteger(offset) || offset < 0 || offset > 2_147_483_647 ||
      !Number.isSafeInteger(maximumTargets) || maximumTargets < 1 || maximumTargets > 256) return fail();
  if (!recordedPassBoundaryMatches(value, sources, boundary.recording, boundary)) return fail();
  if (boundary.state !== 'AvailableOriginalJson' || !boundary.originalBoundary || !value.sourceMapDigest) return null;
  const refs = boundary.originalBoundary.originalBodies.filter(ref => ref.role === 'source-map' && ref.domain === 'coglatas.semantic-source-map/1');
  if (refs.length !== 1 || !sameDigest(refs[0].digest, value.sourceMapDigest)) return null;
  const body = boundary.bodies.find(item => item.key === refs[0].domain + ':' + refs[0].digest.value);
  if (!body || body.domain !== refs[0].domain || !sameDigest(body.digest, refs[0].digest)) return null;
  const map: unknown = JSON.parse(body.text);
  if (!object(map) || map['format'] !== 'coglatas.semantic-source-map/1' || map['compilerInputId'] !== value.inputId ||
      !Array.isArray(map['nodes']) || !Array.isArray(map['relations']) || !equal(map, value.sourceMap)) return fail();
  const nodes = map['nodes']; const relations = map['relations']; const totalTargets = nodes.length + relations.length;
  const targets: PassBoundarySourceTarget[] = [];
  for (let index = offset; index < totalTargets && targets.length < maximumTargets; index++) {
    const kind = index < nodes.length ? 'node' : 'relation';
    const entry: unknown = kind === 'node' ? nodes[index] : relations[index - nodes.length];
    if (!object(entry) || typeof entry[kind === 'node' ? 'irNodeId' : 'relationId'] !== 'string' ||
        !isProjectLocationShape(entry['location'])) return fail();
    const location = entry['location'];
    if (typeof resolveLocation(value, location, true) === 'string') return fail();
    targets.push(Object.freeze({kind, identity: entry[kind === 'node' ? 'irNodeId' : 'relationId'] as string, location: freeze(location)}));
  }
  if (!recordedPassBoundaryMatches(value, sources, boundary.recording, boundary)) return fail();
  const page: PassBoundarySourcePage = Object.freeze({offset, maximumTargets, totalTargets,
    originalMapDigest: refs[0].digest, originalRecordDigest: boundary.originalRecordDigest, targets: Object.freeze(targets)});
  bindings.set(page, {value, sources, boundary}); return page;
}
