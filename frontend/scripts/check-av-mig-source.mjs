import { dirname, resolve } from 'node:path';
import { readFileSync, readdirSync } from 'node:fs';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

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
  static property(node, name) {
    return node.properties.find((item) => item.name?.getText() === name)?.initializer;
  }
  static walkRoutes(array, prefix = '') {
    assert.ok(array && ts.isArrayLiteralExpression(array), 'Unsupported dynamic route array');
    return array.elements.flatMap((entry) => {
      assert.ok(ts.isObjectLiteralExpression(entry), 'Unsupported route expression');
      const children = SourceInventory.property(entry, 'children'),
        fragment = SourceInventory.property(entry, 'path'),
        joined = [prefix, fragment?.text].filter(Boolean).join('/');
      assert.ok(fragment && ts.isStringLiteral(fragment), 'Unsupported dynamic route path');
      if (children) {
        return SourceInventory.walkRoutes(children, joined);
      }
      if (joined === '**') {
        return ['**'];
      }
      return [`/${joined}`];
    });
  }
  static routesFrom(source) {
    const declaration = source.statements
      .filter(ts.isVariableStatement)
      .flatMap((statement) => [...statement.declarationList.declarations])
      .find((item) => item.name.getText() === 'routes');
    return SourceInventory.walkRoutes(declaration?.initializer);
  }
  static classSources() {
    const sources = new Map();
    for (const file of readdirSync(resolve(SourceInventory.root, 'frontend/src/app'), {
      recursive: true,
    }).filter(
      (name) => name.endsWith('.ts') && !name.endsWith('.spec.ts') && !name.endsWith('.stories.ts'),
    )) {
      const path = resolve(SourceInventory.root, 'frontend/src/app', file);
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
    const { owners, sources } = context,
      declared = new Set([
        route.component,
        ...SourceInventory.injectedOwners(
          SourceInventory.parse(resolve(SourceInventory.root, row.ownerPath)),
          owners,
          sources,
        ),
      ]);
    assert.equal(
      sources.get(route.component),
      resolve(SourceInventory.root, row.ownerPath),
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
  static verify(data, routeSource) {
    const [inventory, freeze, target] = data,
      freezeByPath = new Map(freeze.routes.map((route) => [route.path, route])),
      owners = new Map(inventory.stateOwners.map((owner) => [owner.name, owner])),
      paths = SourceInventory.unique(
        SourceInventory.routesFrom(
          routeSource ??
            SourceInventory.parse(resolve(SourceInventory.root, freeze.routeDefinition)),
        ),
        'production routes',
      ),
      sources = SourceInventory.classSources();
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
        paths,
        `${label}: production route drift`,
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
    for (const route of inventory.routes) {
      SourceInventory.verifyRow(route, freezeByPath.get(route.path), { owners, sources });
    }
    return paths.length;
  }
  static loadInputs(path) {
    if (path) { return JSON.parse(readFileSync(path, 'utf8')); }
    return SourceInventory.inputs();
  }
  static inputs() {
    return [
      SourceInventory.read('docs/migration/avalonia/angular-frontend-inventory.json'),
      SourceInventory.read('docs/migration/avalonia/angular-feature-freeze-matrix.json'),
      SourceInventory.read('docs/migration/avalonia/angular-target-surface-map-v5.8.1.json'),
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
