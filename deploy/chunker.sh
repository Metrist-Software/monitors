#!/bin/bash
#
# Chunk monitors in build groups
#
set -vx

base=$(cd "$(dirname "$0")"; (cd .. && /bin/pwd))

# Slow monitors get their own "chunk", what's left goes into runner count.
# So if we want 12 runners, runner count is "12 - <number of entries in own_chunks>"
own_chunks="snowflake twiliovidclient zoomclient manifests datadog-tests"
runner_count=7
excluded_monitor="testjsmonitor testpythonmonitor testdotnetmonitor"

exclude_pat=$(echo $own_chunks | sed 's/ /|/g')
exclude_monitor_pat=$(echo $excluded_monitor | sed 's/ /|/g')
include_pat=$(echo \"$own_chunks\" | jq -cM '. | split(" ") | map(split("."))')

cd $base
monitors=$(ls -d Metrist.Runner **/Metrist.Monitors.* **/monitor.* |
               awk -F. '{print tolower($(NF))}' |
               grep -Ev "^($exclude_monitor_pat)\$" |
               grep -Ev "^($exclude_pat)\$")

# Calculate the 'randomized' chunks
ids=$(echo \"$monitors\" | jq -cM --arg runner_count "$runner_count" \
      '. | split(" ") | [_nwise(length / ($runner_count | tonumber ) | ceil)]')

# Splice the fixed chunks back in
ids=$(echo $ids | jq -cM ". + $include_pat")

echo "::set-output name=build-chunks::$ids"
