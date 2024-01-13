export CLOUD_PLATFORM ?= az
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

# This is more documentation than something we need to do regularly. Maybe if we need to
# monitor multiple zones this becomes relevant, until then, the monitor is hardcoded on a single
# version of a single package anyway, like the NPM one.
publish_artifact: check_token dev
	cd hello_world; npm publish

# Azure cannot do a reasonable form of auth for the DevOps packages stuff, so we need to do it the hard way
# through .npmrc and a lot of code and a personal access token.
# https://docs.microsoft.com/en-us/azure/devops/artifacts/npm/npmrc?view=azure-devops has some details.
check_token:
	@which envsubst >/dev/null || (echo "\nPlease install 'envsubst'\n"; exit 1)
	@test -n "$$AZDO_PAT" || (echo "\nPlease set your Azure DevOps PAT in AZDO_PAT (base64 encoded), see Makefile for details\n"; exit 1)
	cd hello_world; envsubst <.npmrc.in >.npmrc

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m azuredevopsartifacts \
		-s Ping \
		-s DownloadPackage
