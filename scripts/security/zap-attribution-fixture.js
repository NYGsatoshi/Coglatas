/* global Java */
// Native integration fixture only; this is not a product scan or historical proof.
const classes = {Alert: Java.type('org.parosproxy.paros.core.scanner.Alert'),
  AlertFilter: Java.type('org.zaproxy.zap.extension.alertFilters.AlertFilter'),
  Control: Java.type('org.parosproxy.paros.control.Control'),
  Files: Java.type('java.nio.file.Files'), HashSet: Java.type('java.util.HashSet'),
  HistoryReference: Java.type('org.parosproxy.paros.model.HistoryReference'),
  HttpMessage: Java.type('org.parosproxy.paros.network.HttpMessage'),
  Model: Java.type('org.parosproxy.paros.model.Model'),
  Paths: Java.type('java.nio.file.Paths'),
  System: Java.type('java.lang.System'), URI: Java.type('org.apache.commons.httpclient.URI')},
  confidence = 2, expectedAlerts = 1, highRisk = 3, piiRule = 10062;

class Fixture {
  static message(query = false) {
    const message = new classes.HttpMessage(new classes.URI(query ? 'http://app:8080/api/admin/invites?page=1&pageSize=50' : 'http://app:8080/api/files', true));
    message.getResponseHeader().setMessage('HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n');
    message.setResponseBody(query ? '{"traceId":"00-abcdef123456789012abcdef-0123456789abcdef-00"}' : '{"items":[{"id":"aaaaaaaa-bbbb-cccc-dddd-123456789012"}]}');
    return message;
  }

  static raise(query = false) {
    const alert = new classes.Alert(piiRule, highRisk, confidence, 'Synthetic attribution qualification'),
      extension = classes.Control.getSingleton().getExtensionLoader().getExtension('ExtensionAlert'),
      message = Fixture.message(query), reference = new classes.HistoryReference(classes.Model.getSingleton().getSession(),
        classes.HistoryReference.TYPE_ZAP_USER, message);
    alert.setUri(String(message.getRequestHeader().getURI()));
    alert.setEvidence('123456789012');
    alert.setMessage(message);
    alert.setHistoryRef(reference);
    extension.alertFound(alert, reference);
    if (extension.getAllAlerts().size() !== expectedAlerts) { throw new Error('Synthetic alert did not retain its session history'); }
  }

  static match(filter, uri, method = 'GET', rule = piiRule) {
    const alert = new classes.Alert(rule, highRisk, confidence, 'Synthetic filter scope'),
      message = new classes.HttpMessage(new classes.URI(uri, true));
    message.getRequestHeader().setMethod(method);
    alert.setUri(uri);
    alert.setMessage(message);
    return filter.appliesToAlert(alert);
  }

  static qualifyFilter() {
    const context = classes.Model.getSingleton().getSession().getNewContext('sec06-api'),
      filter = new classes.AlertFilter(), methods = new classes.HashSet(),
      plan = JSON.parse(String(classes.Files.readString(classes.Paths.get('/state/invites-filter.json')))),
      route = 'http://app:8080/api/admin/invites';
    if (plan.ruleId !== piiRule || plan.context !== 'sec06-api' || plan.methods.join(',') !== 'GET' || !plan.urlRegex) {
      throw new Error('Native filter fixture received broadened plan scope');
    }
    context.addIncludeInContextRegex('http://app:8080/.*');
    methods.add('GET');
    filter.setEnabled(true);
    filter.setContextId(context.getId());
    filter.setRuleId(String(plan.ruleId));
    filter.setMethods(methods);
    filter.setUrl(route);
    filter.setUrlRegex(false);
    if (!Fixture.match(filter, route) || Fixture.match(filter, `${route}?page=1&pageSize=50`)) {
      throw new Error('Original exact full-URI mismatch did not reproduce');
    }
    filter.setUrl(plan.url.replace('${COGLATAS_SECURITY_ZAP_TARGET_REGEX}', 'http://app:8080'));
    filter.setUrlRegex(plan.urlRegex);
    for (const query of ['', '?page=1', '?pageSize=50', '?page=1&pageSize=50', '?pageSize=50&page=1']) {
      if (!Fixture.match(filter, route + query)) { throw new Error('Native filter missed contract query'); }
    }
    for (const method of ['POST', 'PUT', 'PATCH', 'DELETE']) {
      if (Fixture.match(filter, route, method)) { throw new Error('Native filter covered other method'); }
    }
    for (const uri of [`${route}/other`, `${route}?other=1`, `${route}?page=1&page=2`,
      'http://app:8080/api/admin/users', 'http://app:8080/api/tasks?page=1&pageSize=50',
      'http://other:8080/api/admin/invites']) {
      if (Fixture.match(filter, uri)) { throw new Error('Native filter covered other URI'); }
    }
    if (Fixture.match(filter, route, 'GET', piiRule + expectedAlerts)) { throw new Error('Native filter covered other rule'); }
    context.addExcludeFromContextRegex('http://app:8080/api/admin/invites.*');
    if (Fixture.match(filter, route)) { throw new Error('Native filter ignored context exclusion'); }
    print('Pinned native AlertFilter: old query mismatch and new rule/method/origin/path/query/context scope PASS');
  }

  static run() {
    const caseName = String(classes.System.getenv('COGLATAS_SECURITY_ATTRIBUTION_CASE'));
    if (!['clean', 'high', 'query-high'].includes(caseName)) { throw new Error('Synthetic fixture case missing'); }
    Fixture.qualifyFilter();
    if (caseName !== 'clean') { Fixture.raise(caseName === 'query-high'); }
  }
}

Fixture.run();
