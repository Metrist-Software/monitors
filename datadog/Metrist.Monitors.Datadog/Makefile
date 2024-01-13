include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	  test -f priv/runner/Metrist.Runner || ./install-runner.sh;\
	  mix metrist.run_monitor -t rundll \
			-l "$(PWD)/bin/Release/net6.0/linux-x64/publish"\
			-m datadog \
			-s SubmitEvent \
			-s GetEvent \
			--timeout 120 \
			--extra-config EnvironmentTagName=$(ENVIRONMENT_TAG):$(ORCHESTRATOR_REGION) \
			--extra-config ApiKey=@env@:@secret@:/$(ENVIRONMENT_TAG)/datadog/credentials#apiKey \
			--extra-config AppKey=@env@:@secret@:/$(ENVIRONMENT_TAG)/datadog/credentials#applicationKey
