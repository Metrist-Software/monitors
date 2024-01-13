#!/usr/bin/env bash

# Running with no arguments will build everything
# Otherwise pass a list of monitor logical names and/or `runner` to build all provided

set -eo pipefail
shopt -s nullglob

baseDir=$(cd "$(dirname "$0")"; (cd .. && /bin/pwd))
publish_dir=bin/Release/net6.0/linux-x64/publish/

case "$GITHUB_REF" in
    refs/heads/main)
        qualifier=""
        ;;
    *)
        qualifier="-preview"
        ;;
esac

shortrev=$(git rev-parse --short HEAD)
VERSION=$(cat "$baseDir/VERSION")-$(date +%Y%m%d%H%M%S)-$shortrev

logical_name() {
    # This heuristic should work until it breaks. We probably want to have a
    # config file
    echo $1|sed 's/.*\.//'|tr '[:upper:]' '[:lower:]'
}

s3_upload() {
    for i in $@; do
        echo "Uploading $i"
        aws s3 cp "$i" s3://canary-public-assets/dist/monitors/;
    done
}

package_runner() {
    #
    #  Publish the C# Monitor runner. This is used by the CMA and installed from S3, so we don't
    #  need a "fancy" packaging method; a .ZIP file is fine.
    #
    echo "=== Publishing Runner ..."
    cd "$baseDir/Metrist.Runner"
    target=$PWD

    make VERSION=$VERSION QUALIFIER=$qualifier package

    s3_upload "$target/runner-$VERSION-linux-x64.zip" "$target/runner-latest$qualifier.txt"
}

package_monitor() {
    echo "=== Publishing $1 ..."
    cd "$baseDir/$1"
    target=$PWD

    set -vx
    make VERSION=$VERSION QUALIFIER=$qualifier package

    s3_upload "$target/$2-$VERSION-linux-x64.zip" "$target/$2-latest$qualifier.txt"
}

publish_manifests() {
    #
    #  Combines monitor-manifests '**/monitor-manifest.json' and package
    #  manifests '**/manifest.json' then concatenates them into a single file that
    #  is published at assets.metrist.io/dist/monitors/manifests$qualifier.json
    #
    #  Format: { monitors:[{ logical_name: name, packages: [] }] }
    echo "=== Publishing Manifests ..."
    manifestfilename=$baseDir/manifests$qualifier.json
    manifest_collection="{}"
    cd "$baseDir"
    for mon in */
    do
      cd "$baseDir/$mon"
      if [[ -f "monitor-manifest.json" ]]
      then
        combined_manifest=$(cat monitor-manifest.json)
        for package in **/manifest.json
        do
          combined_manifest=$(echo "$combined_manifest" | jq --argjson packageJson "$(cat $package)" '.packages += [$packageJson]')
        done

        manifest_collection=$(echo "$manifest_collection" | jq --argjson monitorJson "${combined_manifest}" '.monitors += [$monitorJson]' | tee $manifestfilename)
      fi
    done

    echo "$manifest_collection"

    s3_upload "$manifestfilename"
}

publish_datadog_tests() {
    #
    #  Concatenates '**/*.datadog.json' into a single file
    #  that is published at s3://metrist-private-assets/datadog/
    #
    echo "=== Publishing Datadog Tests ..."
    cd "$baseDir"
    filename=datadog-synthetics$qualifier.json
    manifest_collection=$(find . -name '*.datadog.json' -print0 | xargs -0 cat | jq -s  '.' | tee $filename)

    echo $filename

    echo "Uploading $filename"
    aws s3 cp "$filename" s3://metrist-private-assets/datadog/;
}

cd $baseDir
echo "Using node $(node --version); npm $(npm --version); dotnet $(dotnet --version)"

build_runner=true
publish_manifests=true
publish_datadog_tests=true

if [ $# -eq 0 ]; then
    for i in **/Metrist.Monitors.* **/monitor.*
    do
        monitors_to_build+=($(logical_name $i))
    done
else
    monitors_to_build=$@

    # "runner" is a special argument to trigger dotnet DLL runner publishing.
    if [[ ! " ${monitors_to_build[*]} " =~ " runner " ]]; then
        build_runner=false
    fi

    # "manifests" is a special argument to trigger manifest publishing.
    if [[ ! " ${monitors_to_build[*]} " =~ " manifests " ]]; then
        publish_manifests=false
    fi

    # "datadog-tests" is a special argument to trigger datadog-test json publishing.
    if [[ ! " ${monitors_to_build[*]} " =~ " datadog-tests " ]]; then
        publish_datadog_tests=false
    fi
fi

if [ "$build_runner" = true  ]
then
    echo "Packaging runner"
    package_runner
fi

#
#  Publish all manifests
#
if [ "$publish_manifests" = true  ]
then
    echo "Publishing manifests"
    publish_manifests
fi

#
#  Publish datadog tests
#
if [ "$publish_datadog_tests" = true  ]
then
    echo "Publishing datadog tests"
    publish_datadog_tests
fi


#
#  Publish all monitor packages
#
cd "$baseDir"
for mon in **/Metrist.Monitors.* **/monitor.*
do
    logical=$(logical_name $mon)

    if [[ ! " ${monitors_to_build[*]} " =~ " $logical " ]]; then
        continue
    fi

    echo "Packaging $logical"

    package_monitor $mon $logical
    cd "$baseDir"
done

echo "Waiting for background packaging to complete..."
wait

# If this isn't being run by github actions, then go ahead and invalidate.
# CI env var is always set to true in github actions
if [[ -z $CI ]]; then
  echo "Running cloudfront invalidation"
  deploy/invalidate_cloudfront.sh
fi

echo "All done!"
