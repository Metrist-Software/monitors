include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m heroku \
		-s AppPing \
		-s ConfigUpdate \
		-e ApiKey=@env@:@secret@:/$(ENVIRONMENT_TAG)/monitors/heroku/$(ORCHESTRATOR_REGION)/secrets#apiKey \
		-e ApiKeyUser=@env@:@secret@:/$(ENVIRONMENT_TAG)/monitors/heroku/$(ORCHESTRATOR_REGION)/secrets#apiKeyUser \
		-e AppName=@env@:@secret@:/$(ENVIRONMENT_TAG)/monitors/heroku/$(ORCHESTRATOR_REGION)/secrets#appName \
		-e Region=@env@:$(ORCHESTRATOR_REGION)
