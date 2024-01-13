include ../../Makefile.common
include ../../Makefile.nodets

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js" \
		-m twiliovid \
		-s JoinRoom \
		-e AccountSid=@secret@:@env@:/$(ENVIRONMENT_TAG)/twilio/api-token#accountSid \
		-e ApiKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/twilio/api-token#apiKey \
		-e ApiSecret=@secret@:@env@:/$(ENVIRONMENT_TAG)/twilio/api-token#apiSecret \
		-e AuthToken=@secret@:@env@:/$(ENVIRONMENT_TAG)/twilio/api-token#authToken
