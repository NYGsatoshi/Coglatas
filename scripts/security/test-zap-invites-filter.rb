#!/usr/bin/env ruby
require "yaml"

# Models AlertFilter's full-URI String.equals / String.matches semantics.
# Native ZAP qualification remains independently required; this is deterministic.
expected = {
  "ruleId" => 10062, "ruleName" => "PII Disclosure", "newRisk" => "False Positive",
  "context" => "sec06-api", "methods" => ["GET"], "urlRegex" => true,
  "url" => '^${COGLATAS_SECURITY_ZAP_TARGET_REGEX}/api/admin/invites(?:[?](?:page=-?(?:0|[1-9][0-9]*)(?:&pageSize=-?(?:0|[1-9][0-9]*))?|pageSize=-?(?:0|[1-9][0-9]*)(?:&page=-?(?:0|[1-9][0-9]*))?))?$'
}
plan = YAML.safe_load(File.read(File.join(__dir__, "zap-automation.yaml")), aliases: false)
filters = plan.fetch("jobs").select { |job| job["type"] == "alertFilter" }
raise "Expected exactly one alertFilter job" unless filters.length == 1
items = filters.first.fetch("alertFilters")
filter = items.select { |item| item["methods"] == ["GET"] }
raise "Invites filter scope changed" unless filter == [expected]

def matches?(filter, uri, method = "GET", rule = 10062, context = "sec06-api")
  return false unless filter["ruleId"] == rule && filter["context"] == context && filter["methods"].include?(method)
  url = filter.fetch("url").gsub('${COGLATAS_SECURITY_ZAP_TARGET_REGEX}', Regexp.escape("http://app:8080"))
    .gsub('${COGLATAS_SECURITY_ZAP_TARGET}', "http://app:8080")
  return url == uri unless filter["urlRegex"]
  match = Regexp.new(url).match(uri)
  match && match.begin(0) == 0 && match.end(0) == uri.length
end

route = "http://app:8080/api/admin/invites"
old = expected.merge("url" => '${COGLATAS_SECURITY_ZAP_TARGET}/api/admin/invites', "urlRegex" => false)
raise "Original exact filter reproduction failed" unless matches?(old, route) && !matches?(old, route + "?page=1&pageSize=50")
positive = ["", "?page=1", "?pageSize=50", "?page=1&pageSize=50", "?pageSize=50&page=1",
  "?page=0&pageSize=0", "?page=-1&pageSize=-50", "?page=2147483647&pageSize=200"]
positive.each { |suffix| raise "Legitimate query missed: #{suffix}" unless matches?(expected, route + suffix) }
negative = ["/other", "/", "?", "?page=", "?page=01", "?page=+1", "?page=1.0", "?page=1&other=50",
  "?other=1", "?page=1&page=2", "?pageSize=50&pageSize=50", "?page=1&pageSize=50&other=1",
  "?page=1&pageSize=50&", "?page=1#fragment", "?Page=1", "?page=%31", "?page=1\n"]
negative.each { |suffix| raise "Unexpected query/path filtered: #{suffix}" if matches?(expected, route + suffix) }
%w[POST PUT PATCH DELETE HEAD OPTIONS].each { |method| raise "Other method filtered" if matches?(expected, route, method) }
["http://app:8080/api/admin/users?page=1&pageSize=50", "http://app:8080/api/tasks?page=1&pageSize=50",
 "http://other:8080/api/admin/invites?page=1", "https://app:8080/api/admin/invites", "http://app:80800/api/admin/invites"].each do |uri|
  raise "Neighbor/origin filtered" if matches?(expected, uri)
end
raise "Other rule filtered" if matches?(expected, route, "GET", 40012)
raise "Other context filtered" if matches?(expected, route, "GET", 10062, "other")
# Any unmatched High still reaches the unchanged host High blocker.
raise "Unrelated High suppressed" if matches?(expected, "http://app:8080/api/tasks", "GET", 10062)
mutations = [expected.merge("url" => route + ".*"), expected.merge("url" => ".*"),
  expected.merge("url" => "["), expected.merge("url" => expected["url"].delete_prefix("^")),
  expected.merge("urlRegex" => false), expected.merge("methods" => ["GET", "POST"]),
  expected.merge("ruleId" => -1), expected.merge("context" => "")]
mutations.each { |mutant| raise "Broadened/malformed mutation accepted" if mutant == expected }
puts "SEC-06 invites full-URI match and negative scope contracts PASS"
