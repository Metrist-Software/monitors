export CLOUD_PLATFORM ?= gcp
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	GCP_ZONE=us-west1-a mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node --trace-warnings build/main.js" \
		-m gcpcomputeengine \
		-s CreateInstance -s GetInstanceInfo -s DeleteInstance \
		--timeout 240 \
		-e PrivateKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/gcpcomputeengine/secrets#private_key \
		-e ProjectId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/gcpcomputeengine/secrets#project_id
