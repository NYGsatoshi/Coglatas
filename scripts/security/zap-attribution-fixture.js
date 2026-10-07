/* global Java, print */
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

// Exercise the installed PiiScanRule candidate/parser/card/Luhn predicate.
// These are synthetic classifier controls, not a product acceptance scan.
class Fixture {
  static detectorMethod(type, name, ...parameters) {
    const method = type.getDeclaredMethod(name, ...parameters);
    method.setAccessible(true);
    return method;
  }

  static detectorSource() {
    const card = Array.from(Java.type('org.zaproxy.zap.extension.pscanrules.PiiScanRule').class.getDeclaredClasses())
      .find(type => String(type.getSimpleName()) === 'CreditCard'),
      rule = Java.type('org.zaproxy.zap.extension.pscanrules.PiiScanRule').class,
      string = Java.type('java.lang.String').class,
      threshold = Java.type('org.parosproxy.paros.core.scanner.Plugin$AlertThreshold');
    if (!card) { throw new Error('Pinned card detector unavailable'); }
    return {card, decimal: Fixture.detectorMethod(rule, 'isDecimal', string),
      numbers: Fixture.detectorMethod(rule, 'getNumberSequences', string, threshold.class),
      scientific: Fixture.detectorMethod(rule, 'isSci', string), string, threshold};
  }

  static matchesPiiCandidate(candidate, source, threshold) {
    const luhn = Java.type('org.zaproxy.addon.commonlib.PiiUtils'),
      matcherMethod = Fixture.detectorMethod(source.card, 'matcher', source.string),
      number = Fixture.detectorMethod(candidate.getClass(), 'getCandidate').invoke(candidate),
      surrounding = Fixture.detectorMethod(candidate.getClass(), 'getContainingString').invoke(candidate);
    if ((source.decimal.invoke(null, surrounding) && threshold !== source.threshold.LOW) ||
        source.scientific.invoke(null, surrounding) || !luhn.isValidLuhn(number)) { return false; }
    return Array.from(source.card.getEnumConstants()).some(card => matcherMethod.invoke(card, number).find());
  }

  static matchesPii(body, threshold) {
    const source = Fixture.detectorSource(), values = source.numbers.invoke(null, body, threshold);
    for (const candidate of values) {
      if (Fixture.matchesPiiCandidate(candidate, source, threshold)) { return true; }
    }
    return false;
  }

  static qualifyDetector() {
    const encoded = 'trace-v1:MDAtYWJj.ZGVmYWI0.MTExMTEx.MTExMTEx.MTExY2Rl.ZmFiY2Qt.YWJjZGVm.MTIzNDU2.Nzg5MC0w.MQ',
      raw = '00-abcdefab4111111111111111cdefabcd-abcdef1234567890-01',
      source = Fixture.detectorSource();
    for (const threshold of [source.threshold.LOW, source.threshold.MEDIUM]) {
      if (!Fixture.matchesPii(JSON.stringify({traceId: raw}), threshold) ||
          Fixture.matchesPii(JSON.stringify({traceId: encoded}), threshold) ||
          !Fixture.matchesPii(JSON.stringify({protectedData: '4111111111111111', traceId: encoded}), threshold)) {
        throw new Error('Pinned correlation/PII detector contract failed');
      }
    }
    print('Pinned native PiiScanRule: raw trace collision, public representation, and genuine PII detection PASS');
  }
  static uri(query) {
    if (query) { return 'http://app:8080/api/admin/invites?page=1&pageSize=50'; }
    return 'http://app:8080/api/files';
  }

  static body(query) {
    if (query) { return '{"traceId":"00-abcdef123456789012abcdef-0123456789abcdef-00"}'; }
    return '{"items":[{"id":"aaaaaaaa-bbbb-cccc-dddd-123456789012"}]}';
  }

  static message(query = false) {
    const message = new classes.HttpMessage(new classes.URI(Fixture.uri(query), true));
    message.getResponseHeader().setMessage('HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n');
    message.setResponseBody(Fixture.body(query));
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

  static match(filter, uri, {method = 'GET', rule = piiRule} = {}) {
    const alert = new classes.Alert(rule, highRisk, confidence, 'Synthetic filter scope'),
      message = new classes.HttpMessage(new classes.URI(uri, true));
    message.getRequestHeader().setMethod(method);
    alert.setUri(uri);
    alert.setMessage(message);
    return filter.appliesToAlert(alert);
  }

  static filterFromPlan() {
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
    return {context, filter, plan, route};
  }

  static qualifyOld(filter, route) {
    filter.setUrl(route);
    filter.setUrlRegex(false);
    if (!Fixture.match(filter, route) || Fixture.match(filter, `${route}?page=1&pageSize=50`)) {
      throw new Error('Original exact full-URI mismatch did not reproduce');
    }
  }

  static requireMatches(filter, uris) {
    for (const uri of uris) {
      if (!Fixture.match(filter, uri)) { throw new Error('Native filter missed contract query'); }
    }
  }

  static requireMisses(filter, uris, options = {}) {
    for (const uri of uris) {
      if (Fixture.match(filter, uri, options)) { throw new Error('Native filter covered unreviewed scope'); }
    }
  }

  static qualifyScope(context, filter, route) {
    for (const method of ['POST', 'PUT', 'PATCH', 'DELETE']) {
      Fixture.requireMisses(filter, [route], {method});
    }
    Fixture.requireMisses(filter, [`${route}/other`, `${route}?other=1`, `${route}?page=1&page=2`,
      'http://app:8080/api/admin/users', 'http://app:8080/api/tasks?page=1&pageSize=50',
      'http://other:8080/api/admin/invites']);
    Fixture.requireMisses(filter, [route], {rule: piiRule + expectedAlerts});
    context.addExcludeFromContextRegex('http://app:8080/api/admin/invites.*');
    Fixture.requireMisses(filter, [route]);
  }

  static qualifyFilter() {
    const {context, filter, plan, route} = Fixture.filterFromPlan();
    Fixture.qualifyOld(filter, route);
    filter.setUrl(plan.url);
    filter.setUrlRegex(plan.urlRegex);
    Fixture.requireMatches(filter, ['', '?page=1', '?pageSize=50', '?page=1&pageSize=50', '?pageSize=50&page=1'].map(query => route + query));
    Fixture.qualifyScope(context, filter, route);
    print('Pinned native AlertFilter: old query mismatch and new rule/method/origin/path/query/context scope PASS');
  }

  static run() {
    const caseName = String(classes.System.getenv('COGLATAS_SECURITY_ATTRIBUTION_CASE'));
    if (!['clean', 'high', 'query-high'].includes(caseName)) { throw new Error('Synthetic fixture case missing'); }
    Fixture.qualifyFilter();
    Fixture.qualifyDetector();
    if (caseName !== 'clean') { Fixture.raise(caseName === 'query-high'); }
  }
}

Fixture.run();
