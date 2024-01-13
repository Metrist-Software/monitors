export CLOUD_PLATFORM ?= az
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js" \
		-m azureappservice \
		-s PingService \
		-e Hostname=@secret@:@env@:/$(ENVIRONMENT_TAG)/az/\$(ORCHESTRATOR_REGION)/azureappservice/credentials#hostname \
		--timeout 300
