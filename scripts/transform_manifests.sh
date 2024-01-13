#!/usr/bin/env bash

# Temp script to transform all existing manifests into manifest/package manifest pairs in the new directory structure

baseDir=$(cd "$(dirname "$0")"; (cd .. && /bin/pwd))
cd "$baseDir"
for mon in **/Metrist.Monitors.* **/monitor.*
do
    echo "Transforming $mon"

    cd "$baseDir/$mon"
    if [[ -f "monitor-manifest.json" ]]
    then
      cat monitor-manifest.json  | jq '{ "description": .description, "logical_name": .logical_name, "name": .name, "groups": .groups, "status_page_url": "", "homepage": "" }' > ../monitor-manifest.json
      cat monitor-manifest.json  | jq '.logical_name as $logical_name | . + {"package_name": .logical_name, steps: [(.steps // [])[]
      | {description, logical_name, name, default_timeout_seconds: .recommended_timeout_seconds, docs_url: ""}],
      "config_values": [(.environment_variables // [])[] | {description, required, name: .name | sub("METRIST_"; "") | sub($logical_name|ascii_upcase; "") | gsub("(^|_)(?<a>[A-Z])(?<b>[A-Z]+)";.a+(.b|ascii_downcase)), environment_variable_name: .name}]}
      | del(.name)
      | del(.groups)
      | del(.environment_variables)' > manifest.json
    else
      echo "monitor-manifest.json not present in $mon.. skipping"
    fi
done

