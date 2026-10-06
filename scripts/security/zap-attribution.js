// Standalone ZAP script: inspect the alert's own message in the ephemeral JVM.
// Only schema-owned property names and finite categories leave this process.
function propertyNames(contract) {
  var names = new Set();
  function walk(value) {
    if (!value || typeof value !== 'object') return;
    if (value.properties && typeof value.properties === 'object') {
      Object.keys(value.properties).forEach(function (name) { names.add(name); });
    }
    Object.keys(value).forEach(function (name) { walk(value[name]); });
  }
  walk(contract);
  return names;
}

function locateEvidence(body, evidence, names) {
  if (!evidence) return {status: 'empty-evidence', locations: []};
  if (body.length > 2097152) return {status: 'body-limit', locations: []};
  var document;
  try { document = JSON.parse(body); }
  catch (_) { return {status: 'non-json-body', locations: []}; }
  var locations = [], limited = false;
  function walk(value, path, depth) {
    if (depth > 32 || locations.length >= 16) { limited = true; return; }
    if (Array.isArray(value)) {
      value.forEach(function (item) { walk(item, path.concat(['*']), depth + 1); });
    } else if (value && typeof value === 'object') {
      Object.keys(value).forEach(function (key) {
        walk(value[key], path.concat([names.has(key) ? key : '__unmodeled__']), depth + 1);
      });
    } else if (value !== null && String(value).indexOf(evidence) !== -1) {
      var category = typeof value === 'string' ? 'text' : typeof value;
      if (typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) {
        category = String(value) === evidence ? 'uuid' : 'uuid-substring';
      }
      locations.push({schemaPath: path, valueCategory: category,
                      matchKind: String(value) === evidence ? 'exact' : 'substring'});
    }
  }
  walk(document, [], 0);
  return {status: limited ? 'location-limit' : (locations.length ? 'matched-json-property' : 'no-scalar-match'), locations: locations};
}

function captureAttribution() {
  var Files = Java.type('java.nio.file.Files');
  var Paths = Java.type('java.nio.file.Paths');
  var UTF8 = Java.type('java.nio.charset.StandardCharsets').UTF_8;
  var System = Java.type('java.lang.System');
  var Control = Java.type('org.parosproxy.paros.control.Control');
  var role = String(System.getenv('COGLATAS_SECURITY_ZAP_ROLE') || '');
  if (['alpha-owner', 'alpha-restricted', 'beta-owner'].indexOf(role) < 0) throw new Error('SEC-06 attribution role missing');
  var contract = JSON.parse(String(Files.readString(Paths.get('/work/artifacts/openapi/coglatas-openapi.json'), UTF8)));
  var names = propertyNames(contract);
  var alerts = Control.getSingleton().getExtensionLoader().getExtension('ExtensionAlert').getAllAlerts();
  var instances = [];
  for (var i = 0; i < alerts.size(); i++) {
    var alert = alerts.get(i);
    if (alert.getPluginId() !== 10062 || alert.getRisk() !== 3) continue;
    if (instances.length >= 500) throw new Error('SEC-06 attribution alert limit exceeded');
    var result;
    try {
      var message = alert.getMessage();
      result = message ? locateEvidence(String(message.getResponseBody()), String(alert.getEvidence() || ''), names)
                       : {status: 'message-unavailable', locations: []};
    } catch (_) { result = {status: 'message-unavailable', locations: []}; }
    instances.push({alertId: alert.getAlertId(), ruleId: '10062', risk: 'High', status: result.status, locations: result.locations});
  }
  var output = JSON.stringify({schemaVersion: 1, role: role, scopeRuleId: '10062', instances: instances});
  Files.writeString(Paths.get('/state/zap-attribution.json'), output, UTF8);
}

// Node contract tests load only the pure reducer; ZAP executes this branch.
if (typeof Java !== 'undefined') captureAttribution();
if (typeof module !== 'undefined') module.exports = {propertyNames: propertyNames, locateEvidence: locateEvidence};
