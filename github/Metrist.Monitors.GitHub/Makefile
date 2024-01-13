include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-t "rundll" \
		-m github \
		-s PullCode \
		-s PushCode \
		-s RemoveRemoteBranch \
		-s PullRequests \
		-s Issues \
		-s Raw \
		-e ApiToken=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/github/secrets#api_token \
		-e Region=@env@:$(ORCHESTRATOR_REGION) \
		-e Repository=ghmonitor \
		-e Username=canmonuser \
		-e Organization=canmonuser

# Organization is optional and in this case set to the same as username but it excercises the use of that config value
