/* global Java */
// Native integration fixture only; this is not a product scan or historical proof.
const classes = {Alert: Java.type('org.parosproxy.paros.core.scanner.Alert'),
  Control: Java.type('org.parosproxy.paros.control.Control'),
  HttpMessage: Java.type('org.parosproxy.paros.network.HttpMessage'),
  System: Java.type('java.lang.System'), URI: Java.type('org.apache.commons.httpclient.URI')},
  confidence = 2, expectedAlerts = 1, highRisk = 3, piiRule = 10062;

class Fixture {
  static message() {
    const message = new classes.HttpMessage();
    message.getRequestHeader().setURI(new classes.URI('http://app:8080/api/files', true));
    message.getRequestHeader().setMethod('GET');
    message.getResponseHeader().setMessage('HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n');
    message.setResponseBody('{"items":[{"id":"aaaaaaaa-bbbb-cccc-dddd-123456789012"}]}');
    return message;
  }

  static raise() {
    const alert = new classes.Alert(piiRule, highRisk, confidence, 'Synthetic attribution qualification'),
      extension = classes.Control.getSingleton().getExtensionLoader().getExtension('ExtensionAlert');
    alert.setUri('http://app:8080/api/files');
    alert.setEvidence('123456789012');
    alert.setMessage(Fixture.message());
    extension.alertFound(alert, null);
    if (extension.getAllAlerts().size() !== expectedAlerts) { throw new Error('Synthetic alert did not retain its session history'); }
  }

  static run() {
    const caseName = String(classes.System.getenv('COGLATAS_SECURITY_ATTRIBUTION_CASE'));
    if (!['clean', 'high'].includes(caseName)) { throw new Error('Synthetic fixture case missing'); }
    if (caseName === 'high') { Fixture.raise(); }
  }
}

Fixture.run();
