export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m jira \
		-s CreateIssue \
		-s DeleteIssue \
		-e ApiToken=@secret@:/$(ENVIRONMENT_TAG)/monitors/jira#ApiToken \
		-e Url=@secret@:/$(ENVIRONMENT_TAG)/monitors/jira#Url \
		-e ProjectKey=@secret@:/$(ENVIRONMENT_TAG)/monitors/jira#ProjectKey
