/* global Java */
// Only schema locations and finite categories leave the ephemeral scanner JVM.
const bodyLimit = 2097152, depthLimit = 32, depthStep = 1, highRisk = 3,
  initialDepth = 0, instanceLimit = 500, locationLimit = 16, piiRule = 10062,
  schemaVersion = 1;

class Attribution {
  static properties(contract) {
    const names = new Set(), walk = value => {
      if (value && typeof value === 'object') {
        if (value.properties && typeof value.properties === 'object') {
          Object.keys(value.properties).forEach(name => names.add(name));
        }
        Object.keys(value).forEach(name => walk(value[name]));
      }
    };
    walk(contract);
    return names;
  }

  static category(value) {
    if (typeof value !== 'string') { return typeof value; }
    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value)) { return 'uuid'; }
    return 'text';
  }

  static walk(document, evidence, names) {
    const state = {limited: false, locations: []}, walk = (value, path, depth) => {
      if (depth > depthLimit || state.locations.length >= locationLimit) {
        state.limited = true;
      } else if (Array.isArray(value)) {
        value.forEach(item => walk(item, path.concat(['*']), depth + depthStep));
      } else if (value && typeof value === 'object') {
        Object.keys(value).forEach(key => {
          let segment = '__unmodeled__';
          if (names.has(key)) { segment = key; }
          walk(value[key], path.concat([segment]), depth + depthStep);
        });
      } else if (value !== null && String(value).includes(evidence)) {
        let category = Attribution.category(value), matchKind = 'substring';
        if (String(value) === evidence) { matchKind = 'exact'; }
        else if (category === 'uuid') { category = 'uuid-substring'; }
        state.locations.push({matchKind, schemaPath: path, valueCategory: category});
      }
    };
    walk(document, [], initialDepth);
    return state;
  }

  static parse(body) {
    try { return {document: JSON.parse(body), status: 'parsed'}; }
    catch { return {document: null, status: 'non-json-body'}; }
  }

  static result(state) {
    if (state.limited) { return {locations: state.locations, status: 'location-limit'}; }
    if (state.locations.length) { return {locations: state.locations, status: 'matched-json-property'}; }
    return {locations: state.locations, status: 'no-scalar-match'};
  }

  static locate(body, evidence, names) {
    if (!evidence) { return {locations: [], status: 'empty-evidence'}; }
    if (body.length > bodyLimit) { return {locations: [], status: 'body-limit'}; }
    const parsed = Attribution.parse(body);
    if (parsed.status !== 'parsed') { return {locations: [], status: parsed.status}; }
    return Attribution.result(Attribution.walk(parsed.document, evidence, names));
  }

  static read(alert, names) {
    try {
      const message = alert.getMessage();
      if (message) { return Attribution.locate(String(message.getResponseBody()), String(alert.getEvidence()), names); }
    } catch { return {locations: [], status: 'message-unavailable'}; }
    return {locations: [], status: 'message-unavailable'};
  }

  static instance(alert, names) {
    const result = Attribution.read(alert, names);
    return {alertId: alert.getAlertId(), locations: result.locations, risk: 'High', ruleId: String(piiRule), status: result.status};
  }

  static context() {
    const api = {Control: Java.type('org.parosproxy.paros.control.Control'), Files: Java.type('java.nio.file.Files'),
      Paths: Java.type('java.nio.file.Paths'), System: Java.type('java.lang.System'),
      utf8: Java.type('java.nio.charset.StandardCharsets').UTF_8},
      contract = JSON.parse(String(api.Files.readString(api.Paths.get('/work/artifacts/openapi/coglatas-openapi.json'), api.utf8))),
      role = String(api.System.getenv('COGLATAS_SECURITY_ZAP_ROLE'));
    if (!['alpha-owner', 'alpha-restricted', 'beta-owner'].includes(role)) { throw new Error('SEC-06 attribution role missing'); }
    return {alerts: api.Control.getSingleton().getExtensionLoader().getExtension('ExtensionAlert').getAllAlerts(), api,
      names: Attribution.properties(contract), role};
  }

  static capture() {
    const context = Attribution.context(), instances = [];
    for (let ordinal = initialDepth; ordinal < context.alerts.size(); ordinal += depthStep) {
      const alert = context.alerts.get(ordinal);
      if (alert.getPluginId() === piiRule && alert.getRisk() === highRisk) {
        if (instances.length >= instanceLimit) { throw new Error('SEC-06 attribution alert limit exceeded'); }
        instances.push(Attribution.instance(alert, context.names));
      }
    }
    context.api.Files.writeString(context.api.Paths.get('/state/zap-attribution.json'),
      JSON.stringify({instances, role: context.role, schemaVersion, scopeRuleId: String(piiRule)}), context.api.utf8);
  }
}

if (typeof Java !== 'undefined') { Attribution.capture(); }
