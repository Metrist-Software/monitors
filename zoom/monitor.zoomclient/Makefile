include ../../Makefile.common
include ../../Makefile.nodets

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js" \
		-m zoom \
		--timeout=120 \
		-s JoinCall \
		-e SdkKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#sdkKey \
		-e SdkSecret=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#sdkSecret \
		-e MeetingNumber=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#meetingNumber \
		-e MeetingPassword=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#meetingPassword
