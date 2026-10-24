#!/bin/sh
set -eu
test "$#" -eq 1
report=$1
jq -e '.SchemaVersion == 2 and (.Results | type) == "array"' "$report" >/dev/null
jq '[.Results[] | (.Vulnerabilities[]? | {severity:.Severity}), (.Misconfigurations[]? | select(.Status == "FAIL") | {severity:.Severity}), (.Secrets[]? | {severity:.Severity})] | group_by(.severity) | map({severity:.[0].severity,count:length})' "$report"
jq -e '[.Results[] | (.Vulnerabilities[]?, .Misconfigurations[]?) | select(.Severity == "HIGH" or .Severity == "CRITICAL")] | length == 0' "$report" >/dev/null
jq -e '[.Results[].Secrets[]?] | length == 0' "$report" >/dev/null
