
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m pagerduty \
		-s CreateIncident \
		-s CheckForIncident \
		-s ReceiveWebhook \
		-s ResolveIncident \
		-e ApiToken=@secret@:/$(ENVIRONMENT_TAG)/monitors/pagerduty#ApiToken \
		-e RoutingKey=@secret@:/$(ENVIRONMENT_TAG)/monitors/pagerduty#RoutingKey \
		-e ServiceId=@secret@:/$(ENVIRONMENT_TAG)/monitors/pagerduty#ServiceId
