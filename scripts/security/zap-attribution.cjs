/* global Java, module */
// Only schema locations and finite categories leave the ephemeral scanner JVM.
const bodyLimit = 2097152, depthLimit = 32, depthStep = 1, highRisk = 3,
  initialDepth = 0, instanceLimit = 500, locationLimit = 16, piiRule = 10062,
  schemaVersion = 2, sqlRule = 40018;

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

  static mediaType(header) {
    const value = header.getHeader('Content-Type');
    if (!value) { return 'unavailable'; }
    const media = String(value).split(';')[0].trim().toLowerCase();
    if (['application/json', 'application/problem+json'].includes(media)) { return 'json'; }
    if (media === 'text/html') { return 'html'; }
    return 'other';
  }

  static bodyClassification(message, statusCode, mediaType) {
    let body;
    try {
      const responseBody = message.getResponseBody();
      if (responseBody === null || responseBody === undefined) { return 'body-unavailable'; }
      body = String(responseBody);
    }
    catch { return 'body-unavailable'; }
    if (body.length > bodyLimit) { return 'body-limit'; }
    const parsed = Attribution.parse(body);
    if (parsed.status !== 'parsed') { return 'non-json'; }
    const value = parsed.document;
    // Match only the fixed API 404 envelope; no response value is exported.
    if (statusCode === 404 && mediaType === 'json' && value && !Array.isArray(value) &&
        Object.keys(value).length === 3 && Object.keys(value).every(key => ['code', 'message', 'traceId'].includes(key)) &&
        value.code === 'NotFound' && value.message === 'Endpoint not found.' && typeof value.traceId === 'string') {
      return 'endpoint-not-found-envelope';
    }
    return 'other-json';
  }

  static sqlResponse(alert) {
    const result = {alertId: alert.getAlertId(), ruleId: String(sqlRule), risk: 'High',
      status: 'message-unavailable', responseStatusCode: null, mediaType: 'unavailable', bodyClassification: 'body-unavailable'};
    let message;
    try { message = alert.getMessage(); }
    catch { return result; }
    if (!message) { return result; }
    result.status = 'response-header-unavailable';
    try {
      const header = message.getResponseHeader(), code = header.getStatusCode();
      if (!Number.isInteger(code) || code < 100 || code > 599) { return result; }
      result.responseStatusCode = code;
      result.mediaType = Attribution.mediaType(header);
    } catch { return {...result, responseStatusCode: null, mediaType: 'unavailable'}; }
    result.status = 'captured';
    result.bodyClassification = Attribution.bodyClassification(message, result.responseStatusCode, result.mediaType);
    return result;
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
    const context = Attribution.context(), instances = [], sqlResponses = [];
    for (let ordinal = initialDepth; ordinal < context.alerts.size(); ordinal += depthStep) {
      const alert = context.alerts.get(ordinal);
      if (alert.getPluginId() === piiRule && alert.getRisk() === highRisk) {
        if (instances.length >= instanceLimit) { throw new Error('SEC-06 attribution alert limit exceeded'); }
        instances.push(Attribution.instance(alert, context.names));
      }
      if (alert.getPluginId() === sqlRule && alert.getRisk() === highRisk) {
        if (sqlResponses.length >= instanceLimit) { throw new Error('SEC-06 response diagnostic alert limit exceeded'); }
        sqlResponses.push(Attribution.sqlResponse(alert));
      }
    }
    context.api.Files.writeString(context.api.Paths.get('/state/zap-attribution.json'),
      JSON.stringify({instances, role: context.role, schemaVersion, scopeRuleId: String(piiRule), sqlResponses}), context.api.utf8);
  }
}

if (typeof Java !== 'undefined') { Attribution.capture(); }
if (typeof module !== 'undefined') { module.exports = Attribution; }
