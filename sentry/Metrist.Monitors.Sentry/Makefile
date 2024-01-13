include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
#   not include WaitForIssue as Sentry can't hit us with a webhook hit
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m sentry \
		-s CaptureEvent \
		-s WaitForIssue \
		-s ResolveIssue \
		-s DeleteIssue \
		-e ApiToken=@env@:@secret@:/$(ENVIRONMENT_TAG)/monitors/sentry/$(ORCHESTRATOR_REGION)/secrets#apitoken \
		-e IngestUrl=@env@:@secret@:/$(ENVIRONMENT_TAG)/monitors/sentry/$(ORCHESTRATOR_REGION)/secrets#ingesturl \
		-e OrganizationSlug=canary-monitoring-inc \
		-e ProjectSlug=@env@:$(ENVIRONMENT_TAG)-$(ORCHESTRATOR_REGION) \
		-e Region=@env@:$(ORCHESTRATOR_REGION)
