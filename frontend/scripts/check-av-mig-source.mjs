import { dirname, isAbsolute, relative, resolve, sep } from 'node:path';
import { readFileSync, readdirSync, realpathSync } from 'node:fs';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const historicalSourceSnapshot = '4e6a10903a5a472ca89f833aba993a29e1b2cf73';
const currentWebPaths = ['/dm', '/drafts', '/home', '/teams'];
const currentWebOwnerNames = ['DraftsPageComponent', 'HomePageComponent', 'LocalDraftStore', 'LocalFileStarsService'];
const currentWebExtensionPaths = ['/files', '/workspaces/:workspaceId/files'];

const isPathInside = (baseDir, targetPath) => {
  const rel = relative(resolve(baseDir), resolve(targetPath));
  return !(rel === '..' || rel.startsWith(`..${sep}`) || isAbsolute(rel));
};
const resolveInside = (baseDir, input) => {
  const target = resolve(baseDir, input);
  if (!isPathInside(baseDir, target)) {
    throw new Error('Invalid file path');
  }
  // Containment after path normalization alone does not catch symlink escapes.
  if (!isPathInside(realpathSync(baseDir), realpathSync(target))) {
    throw new Error('Invalid file path');
  }
  return target;
};

export class SourceInventory {
  static root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
  static parse(path) {
    return ts.createSourceFile(path, readFileSync(path, 'utf8'), ts.ScriptTarget.Latest, true);
  }
  static read(path) {
    return JSON.parse(readFileSync(resolve(SourceInventory.root, path), 'utf8'));
  }
  static unique(values, label) {
    assert.equal(new Set(values).size, values.length, `${label}: duplicates`);
    return [...values].sort();
  }
  static exactKeys(value, keys, label) {
    const compare = (left, right) => left.localeCompare(right);
    assert.deepEqual(Object.keys(value).sort(compare), [...keys].sort(compare), label);
  }
  static property(node, name) {
    return node.properties.find((item) => item.name?.getText() === name)?.initializer;
  }
  /* eslint-disable max-statements, no-magic-numbers, one-var, sort-keys, sort-vars */
  static componentName(node) {
    if (!node) {
      return null;
    }
    if (ts.isIdentifier(node)) {
      return node.text;
    }
    const candidates = [];
    const visit = (current) => {
      if (ts.isPropertyAccessExpression(current) && current.name.text.endsWith('Component')) {
        candidates.push(current.name.text);
      }
      ts.forEachChild(current, visit);
    };
    visit(node);
    assert.equal(candidates.length, 1, 'Unsupported dynamic route component binding');
    return candidates[0];
  }
  static componentModule(node) {
    if (!node) { return null; }
    const modules = [];
    const visit = (current) => {
      if (ts.isCallExpression(current) && current.expression.kind === ts.SyntaxKind.ImportKeyword) {
        assert.ok(current.arguments.length === 1 && ts.isStringLiteral(current.arguments[0]), 'Unsupported dynamic component module');
        modules.push(current.arguments[0].text);
      }
      ts.forEachChild(current, visit);
    };
    visit(node);
    assert.equal(modules.length, 1, 'Unsupported component module binding');
    return modules[0];
  }
  static routeRecords(array, prefix = '') {
    assert.ok(array && ts.isArrayLiteralExpression(array), 'Unsupported dynamic route array');
    return array.elements.flatMap((entry) => {
      assert.ok(ts.isObjectLiteralExpression(entry), 'Unsupported route expression');
      const children = SourceInventory.property(entry, 'children'),
        fragment = SourceInventory.property(entry, 'path');
      assert.ok(fragment && ts.isStringLiteral(fragment), 'Unsupported dynamic route path');
      const joined = [prefix, fragment.text].filter(Boolean).join('/');
      if (children) {
        return SourceInventory.routeRecords(children, joined);
      }

      const path = joined === '**' ? '**' : `/${joined}`,
        redirect = SourceInventory.property(entry, 'redirectTo'),
        component = SourceInventory.property(entry, 'component'),
        loadComponent = SourceInventory.property(entry, 'loadComponent');

      if (redirect) {
        assert.ok(ts.isStringLiteral(redirect), `${path}: unsupported dynamic redirect`);
        return [{
          path,
          kind: 'redirect',
          redirectTo: redirect.text.startsWith('/') ? redirect.text : `/${redirect.text}`,
        }];
      }

      const componentName = SourceInventory.componentName(component ?? loadComponent);
      if (path === '**') {
        assert.ok(componentName, '**: fallback must declare a component');
        return [{ path, kind: 'fallback', component: componentName }];
      }

      assert.ok(componentName, `${path}: screen must declare component or loadComponent`);
      const data = SourceInventory.property(entry, 'data');
      const directory = data && ts.isObjectLiteralExpression(data)
        ? SourceInventory.property(data, 'conversationDirectory') : null;
      return [{ path, kind: 'screen', component: componentName,
        componentModule: SourceInventory.componentModule(loadComponent),
        conversationDirectory: directory && ts.isStringLiteral(directory) ? directory.text : null }];
    });
  }
  static routeRecordsFrom(source) {
    const declaration = source.statements
      .filter(ts.isVariableStatement)
      .flatMap((statement) => [...statement.declarationList.declarations])
      .find((item) => item.name.getText() === 'routes');
    return SourceInventory.routeRecords(declaration?.initializer);
  }
  static routesFrom(source) {
    return SourceInventory.routeRecordsFrom(source).map((route) => route.path);
  }
  /* eslint-enable max-statements, no-magic-numbers, one-var, sort-keys, sort-vars */
  static sourceFiles() {
    return readdirSync(resolve(SourceInventory.root, 'frontend/src/app'), { recursive: true })
      .filter((name) => name.endsWith('.ts') && !name.endsWith('.spec.ts') && !name.endsWith('.stories.ts'))
      .map((name) => resolve(SourceInventory.root, 'frontend/src/app', name));
  }
  static classSources() {
    const sources = new Map();
    for (const path of SourceInventory.sourceFiles()) {
      for (const node of SourceInventory.parse(path).statements.filter(ts.isClassDeclaration)) {
        if (node.name) {
          sources.set(node.name.text, path);
        }
      }
    }
    return sources;
  }
  static namedImports(source) {
    const imports = new Map();
    for (const item of source.statements
      .filter(ts.isImportDeclaration)
      .filter((entry) => entry.moduleSpecifier.text.startsWith('.'))) {
      const bindings = item.importClause?.namedBindings;
      if (bindings && ts.isNamedImports(bindings)) {
        for (const binding of bindings.elements) {
          imports.set(binding.name.text, {
            name: binding.propertyName?.text ?? binding.name.text,
            path: resolve(dirname(source.fileName), `${item.moduleSpecifier.text}.ts`),
          });
        }
      }
    }
    return imports;
  }
  static injectedOwners(source, owners, sources) {
    const declared = new Set(),
      imports = SourceInventory.namedImports(source),
      visit = (node) => {
        if (ts.isCallExpression(node) && node.expression.getText() === 'inject') {
          const [argument] = node.arguments,
            dependency = imports.get(argument?.getText());
          if (dependency && owners.has(dependency.name)) {
            assert.equal(
              sources.get(dependency.name),
              dependency.path,
              'Injected owner import mismatch',
            );
            declared.add(dependency.name);
          }
        }
        ts.forEachChild(node, visit);
      };
    visit(source);
    return [...declared];
  }
  static verifyScreen(route, row, context) {
    const ownerFile = resolveInside(SourceInventory.root, row.ownerPath);
    const { owners, sources } = context,
      declared = new Set([
        route.component,
        ...SourceInventory.injectedOwners(
          SourceInventory.parse(ownerFile),
          owners,
          sources,
        ),
      ]);
    assert.equal(
      sources.get(route.component),
      ownerFile,
      `${route.path}: component path drift`,
    );
    assert.ok(
      route.stateOwners.includes(route.component),
      `${route.path}: missing component owner`,
    );
    assert.deepEqual(
      [...route.stateOwners].sort(),
      [...declared].sort(),
      `${route.path}: directly injected state owner drift`,
    );
  }
  static verifyRow(route, row, context) {
    assert.ok(route.stateOwners?.length, `${route.path}: missing state owner`);
    SourceInventory.unique(route.stateOwners, `${route.path} owners`);
    assert.equal(
      row.stateOwner,
      route.stateOwners.join(' + '),
      `${route.path}: owner disagreement`,
    );
    for (const name of route.stateOwners) {
      assert.ok(context.owners.has(name), `${route.path}: unknown owner ${name}`);
    }
    if (route.kind === 'screen') {
      SourceInventory.verifyScreen(route, row, context);
    }
    assert.equal(
      route.demoScope === 'november-core',
      row.november,
      `${route.path}: November scope disagreement`,
    );
    assert.equal(
      row.freeze === 'November Required',
      row.november,
      `${route.path}: freeze class disagreement`,
    );
  }
  static verifyCurrentWeb(current, context) {
    assert.ok(current, 'Missing current Web addendum');
    SourceInventory.exactKeys(current, ['schemaVersion', 'historicalSourceSnapshot', 'scope', 'avaloniaPromotion', 'backendContractChanges', 'routes', 'stateOwners', 'routeOwnerExtensions', 'stateClassifications', 'ngrx'], 'Unsupported current Web addendum field');
    assert.equal(current.schemaVersion, 1, 'Unsupported current Web addendum schema');
    assert.equal(current.historicalSourceSnapshot, historicalSourceSnapshot, 'Current Web historical pin drift');
    assert.equal(current.scope, 'current-web-only', 'Current Web scope drift');
    assert.equal(current.avaloniaPromotion, false, 'Current Web cannot promote Avalonia');
    assert.equal(current.backendContractChanges, false, 'Current Web cannot declare backend contracts');
    assert.deepEqual(SourceInventory.unique(current.routes.map((route) => route.path), 'current Web routes'), currentWebPaths, 'Current Web route addendum drift');
    assert.deepEqual(SourceInventory.unique(current.stateOwners.map((owner) => owner.name), 'current Web owners'), currentWebOwnerNames, 'Current Web owner registry drift');
    assert.deepEqual(SourceInventory.unique(current.routeOwnerExtensions.map((row) => row.path), 'current Web owner extensions'), currentWebExtensionPaths, 'Current Web owner extension drift');
    for (const row of current.routeOwnerExtensions) {
      SourceInventory.exactKeys(row, ['path', 'stateOwners'], `${row.path}: unsupported current Web extension field`);
      assert.deepEqual(row.stateOwners, ['LocalFileStarsService'], `${row.path}: current Web owner extension drift`);
    }
    const { sources } = context;
    for (const owner of current.stateOwners) {
      SourceInventory.exactKeys(owner, ['name', 'sourcePath', 'kind'], `${owner.name}: unsupported current Web owner field`);
      assert.equal(sources.get(owner.name), resolveInside(SourceInventory.root, owner.sourcePath), `${owner.name}: current Web source owner drift`);
      assert.ok(!context.owners.has(owner.name), `${owner.name}: current Web owner shadows historical owner`);
      assert.equal(owner.kind, { DraftsPageComponent: 'component-local', HomePageComponent: 'component-local', LocalDraftStore: 'ngrx-component-store', LocalFileStarsService: 'signals-service' }[owner.name], `${owner.name}: current Web owner kind drift`);
    }
    const owners = new Map([...context.owners, ...current.stateOwners.map((owner) => [owner.name, owner])]);
    for (const route of current.routes) {
      const directoryRoute = route.path === '/dm' || route.path === '/teams';
      SourceInventory.exactKeys(route, ['path', 'kind', 'component', 'ownerPath', 'stateOwners', ...(directoryRoute ? ['conversationDirectory'] : [])], `${route.path}: unsupported current Web route field`);
      assert.equal(route.kind, 'screen', `${route.path}: current Web route kind drift`);
      let expectedComponent = 'DraftsPageComponent';
      if (directoryRoute) {
        expectedComponent = 'MessagesPageComponent';
      } else if (route.path === '/home') {
        expectedComponent = 'HomePageComponent';
      }
      assert.equal(route.component, expectedComponent, `${route.path}: current Web component role drift`);
      if (directoryRoute) { assert.equal(route.conversationDirectory, route.path.slice(1), `${route.path}: current Web directory role drift`); }
      SourceInventory.unique(route.stateOwners, `${route.path} current Web owners`);
      for (const owner of route.stateOwners) { assert.ok(owners.has(owner), `${route.path}: unknown current Web owner ${owner}`); }
      SourceInventory.verifyScreen(route, route, { owners, sources });
    }
    return { owners, sources };
  }
  static verifyCurrentWebState(current, context) {
    const entries = new Map(current.stateClassifications.map((entry) => [entry.id, entry]));
    assert.deepEqual(SourceInventory.unique([...entries.keys()], 'current Web state families'), ['local-draft-workbench', 'local-file-stars']);
    assert.equal(entries.size, current.stateClassifications.length, 'Duplicate current Web state family');
    for (const entry of entries.values()) {
      SourceInventory.exactKeys(entry, ['id', 'stateOwner', 'adapter', 'adapterSourcePath', 'storage', 'payloadClass', 'sourceScope', 'authorizationAuthority', 'serverSynchronization', 'cutover', ...(entry.id === 'local-file-stars' ? ['keyFamily'] : [])], `${entry.id}: unsupported current Web state field`);
      assert.ok(context.owners.has(entry.stateOwner), `${entry.id}: unknown state owner`);
      assert.equal(context.sources.get(entry.adapter), resolveInside(SourceInventory.root, entry.adapterSourcePath), `${entry.id}: adapter source drift`);
      assert.equal(entry.sourceScope, 'tenant+user+workspace', `${entry.id}: scope drift`);
      assert.equal(entry.authorizationAuthority, false, `${entry.id}: local state cannot authorize`);
      assert.equal(entry.serverSynchronization, false, `${entry.id}: server synchronization is unavailable`);
      assert.equal(entry.cutover, 'unmapped-web-only', `${entry.id}: cutover promotion is unavailable`);
    }
    assert.equal(entries.get('local-draft-workbench').stateOwner, 'LocalDraftStore');
    assert.equal(entries.get('local-draft-workbench').adapter, 'InMemoryLocalDraftRepository');
    assert.equal(entries.get('local-draft-workbench').storage, 'memory');
    assert.equal(entries.get('local-draft-workbench').payloadClass, 'local-editor-and-synthetic-links');
    assert.equal(entries.get('local-file-stars').stateOwner, 'LocalFileStarsService');
    assert.equal(entries.get('local-file-stars').adapter, 'LocalFileStarsRepository');
    assert.equal(entries.get('local-file-stars').storage, 'localStorage');
    assert.equal(entries.get('local-file-stars').payloadClass, 'versioned-opaque-file-ids-only');
    assert.equal(entries.get('local-file-stars').keyFamily, 'coglatas.local-file-stars.v1:<tenant,user,workspace>');
    assert.equal(current.ngrx.historicalProductionImportsFound, false, 'Historical NgRx fact must remain historical');
    const actualImports = [];
    for (const path of SourceInventory.sourceFiles()) {
      for (const statement of SourceInventory.parse(path).statements.filter(ts.isImportDeclaration)) {
        if (statement.moduleSpecifier.text.startsWith('@ngrx/')) {
          const bindings = statement.importClause?.namedBindings;
          assert.ok(bindings && ts.isNamedImports(bindings), 'Unsupported current NgRx binding');
          for (const binding of bindings.elements) {
            actualImports.push({ sourcePath: relative(SourceInventory.root, path).split(sep).join('/'), module: statement.moduleSpecifier.text, symbol: binding.propertyName?.text ?? binding.name.text, stateOwner: 'LocalDraftStore' });
          }
        }
      }
    }
    assert.deepEqual(current.ngrx.currentProductionImports, [{ sourcePath: context.owners.get('LocalDraftStore').sourcePath, module: '@ngrx/component-store', symbol: 'ComponentStore', stateOwner: 'LocalDraftStore' }], 'Current NgRx addendum drift');
    assert.deepEqual(actualImports, current.ngrx.currentProductionImports, 'Current NgRx source import drift');
    const store = SourceInventory.parse(context.sources.get('LocalDraftStore')).statements
      .find((node) => ts.isClassDeclaration(node) && node.name?.text === 'LocalDraftStore');
    assert.ok(store?.heritageClauses?.some((clause) => clause.token === ts.SyntaxKind.ExtendsKeyword
      && clause.types.some((type) => type.expression.getText() === 'ComponentStore')), 'Current LocalDraftStore ComponentStore binding drift');
  }
  /* eslint-disable max-statements, sort-vars */
  static verify(data, routeSource) {
    const [inventory, freeze, target, current] = data,
      freezeByPath = new Map(freeze.routes.map((route) => [route.path, route])),
      owners = new Map(inventory.stateOwners.map((owner) => [owner.name, owner])),
      declaredRoutes = SourceInventory.routeRecordsFrom(
        routeSource ??
          SourceInventory.parse(resolveInside(SourceInventory.root, freeze.routeDefinition)),
      ),
      declaredByPath = new Map(declaredRoutes.map((route) => [route.path, route])),
      paths = SourceInventory.unique(
        declaredRoutes.map((route) => route.path),
        'production routes',
      ),
      sources = SourceInventory.classSources();
    const historicalPaths = SourceInventory.unique(inventory.routes.map((route) => route.path), 'historical routes');
    assert.equal(historicalPaths.length, 38, 'Historical route inventory must remain the pinned 38 routes');
    assert.equal(inventory.source.commit, historicalSourceSnapshot, 'Historical source pin drift');
    assert.equal(freeze.sourceSnapshot, historicalSourceSnapshot, 'Historical freeze pin drift');
    assert.equal(target.sourceSnapshot, historicalSourceSnapshot, 'Historical target pin drift');
    for (const [label, document] of [
      ['inventory', inventory],
      ['freeze', freeze],
      ['target', target],
    ]) {
      assert.deepEqual(
        SourceInventory.unique(
          document.routes.map((route) => route.path),
          label,
        ),
        historicalPaths,
        `${label}: historical route drift`,
      );
    }
    SourceInventory.unique(
      inventory.stateOwners.map((owner) => owner.name),
      'owner registry',
    );
    for (const owner of owners.values()) {
      if (owner.name !== 'Router') {
        assert.equal(
          sources.get(owner.name),
          resolve(SourceInventory.root, owner.sourcePath),
          `${owner.name}: orphan source owner`,
        );
      }
    }
    const currentContext = SourceInventory.verifyCurrentWeb(current, { owners, sources });
    assert.deepEqual(paths, SourceInventory.unique([...historicalPaths, ...current.routes.map((route) => route.path)], 'historical plus current Web routes'), 'Production routes must equal historical routes plus the current Web addendum');
    const extensions = new Map(current.routeOwnerExtensions.map((row) => [row.path, row.stateOwners]));
    for (const route of inventory.routes) {
      SourceInventory.verifyRow(route, freezeByPath.get(route.path), { owners, sources });
      const declared = declaredByPath.get(route.path);
      assert.ok(declared, `${route.path}: missing production route declaration`);
      assert.equal(declared.kind, route.kind, `${route.path}: route kind drift`);
      if (route.kind === 'redirect') {
        assert.equal(declared.redirectTo, route.redirectTo, `${route.path}: redirect target drift`);
      } else {
        assert.equal(declared.component, route.component, `${route.path}: route component binding drift`);
        if (route.kind === 'screen') {
          SourceInventory.verifyScreen({ ...route, stateOwners: [...route.stateOwners, ...(extensions.get(route.path) ?? [])] }, freezeByPath.get(route.path), currentContext);
        }
      }
    }
    for (const route of current.routes) {
      const declared = declaredByPath.get(route.path);
      assert.equal(declared.kind, route.kind, `${route.path}: current Web route kind drift`);
      assert.equal(declared.component, route.component, `${route.path}: current Web component binding drift`);
      assert.equal(declared.conversationDirectory, route.conversationDirectory ?? null, `${route.path}: current Web directory binding drift`);
      assert.equal(resolveInside(SourceInventory.root, resolve(dirname(resolve(SourceInventory.root, freeze.routeDefinition)), `${declared.componentModule}.ts`)), resolveInside(SourceInventory.root, route.ownerPath), `${route.path}: current Web component module drift`);
    }
    SourceInventory.verifyCurrentWebState(current, currentContext);
    return paths.length;
  }
  /* eslint-enable max-statements, sort-vars */
  static loadInputs(path) {
    if (path) { return JSON.parse(readFileSync(path, 'utf8')); }
    return SourceInventory.inputs();
  }
  static inputs() {
    return [
      SourceInventory.read('docs/migration/avalonia/angular-frontend-inventory.json'),
      SourceInventory.read('docs/migration/avalonia/angular-feature-freeze-matrix.json'),
      SourceInventory.read('docs/migration/avalonia/angular-target-surface-map-v5.8.1.json'),
      SourceInventory.read('docs/verification/current-web-wave-inventory-20261019.json'),
    ];
  }
}
export const inputs = () => SourceInventory.inputs(),
  verify = (data, routeSource) => SourceInventory.verify(data, routeSource);
{
  const [, script, dataPath, routePath] = process.argv;
  if (script && resolve(script) === fileURLToPath(import.meta.url)) {
    try {
      const data = SourceInventory.loadInputs(dataPath),
        routeSource = routePath && SourceInventory.parse(resolve(routePath));
      process.stdout.write(
        `AV-MIG source inventory verified: ${verify(data, routeSource)} routes\n`,
      );
    } catch (error) {
      process.stderr.write(`${error.message}\n`);
      process.exitCode = 1;
    }
  }
}
