ORCHESTRATOR_REGION ?= us-east-1
include ../../Makefile.common
include ../../Makefile.nodets

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js --trace-warnings" \
		-m okta \
		-s GetToken \
		-e ClientId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/okta/$(ORCHESTRATOR_REGION)/secrets#ClientId \
		-e ClientSecret=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/okta/$(ORCHESTRATOR_REGION)/secrets#ClientSecret \
		-e Domain=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/okta/$(ORCHESTRATOR_REGION)/secrets#Domain \
		-e Scope=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/okta/$(ORCHESTRATOR_REGION)/secrets#Scope
