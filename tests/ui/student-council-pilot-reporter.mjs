// Deliberately suppress error values/call logs and attachments: credentials,
// CSRF material, private fixture IDs and response bodies are never CI artifacts.
export default class PilotReporter {
  completed = 0;
  onTestEnd() { this.completed++; }
  onEnd(result) {
    const outcome = this.completed === 0 ? 'NOT_EXECUTED' : result.status === 'passed' ? 'PASS' : 'FAIL';
    process.stdout.write(`Student Council pilot browser execution: ${outcome}\n`);
  }
}
