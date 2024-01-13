export CLOUD_PLATFORM ?= gcp

include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node --trace-warnings build/main.js" \
		-m gcpappengine \
		-s AutoScaleUp \
		-s PingApp \
		-s CreateVersion \
		-s MigrateTraffic \
		-s AutoScaleDown \
		-s DestroyVersion \
		-e PrivateKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/gcpappengine/\$(ORCHESTRATOR_REGION)/secrets#private_key \
		-e ProjectId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/gcpappengine/$(ORCHESTRATOR_REGION)/secrets#project_id \
		-e AppZipUrl=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/gcpappengine/$(ORCHESTRATOR_REGION)/secrets#app_zip_url \
		-e AppHostname=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/gcpappengine/$(ORCHESTRATOR_REGION)/secrets#app_hostname
