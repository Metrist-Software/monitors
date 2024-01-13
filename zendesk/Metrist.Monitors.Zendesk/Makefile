
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m zendesk \
		-s GetUsers \
		-s CreateTicket \
		-s SoftDeleteTicket \
		-s PermanentlyDeleteTicket \
		-e ApiToken=@secret@:@env@:/${ENVIRONMENT_TAG}/zendesk/api-token#token \
		-e Subdomain=@secret@:@env@:/${ENVIRONMENT_TAG}/zendesk/api-token#subdomain
