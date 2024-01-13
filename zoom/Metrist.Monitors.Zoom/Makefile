include ../../Makefile.common
include ../../Makefile.dotnet

test_run: test_run_get_users test_run_create_meeting

test_run_get_users: build
	cd ../../../orchestrator; \
	AWS_BACKEND_REGION=us-east-1 METRIST_CLEANUP_ENABLED=1 mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m zoom \
		-s GetUsers \
		-e ApiKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#apiKey \
		-e ApiSecret=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#apiSecret

# Note: Very restrictive rate limits here at 100 create/delete calls per day per user
test_run_create_meeting: build
	cd ../../../orchestrator; \
	AWS_BACKEND_REGION=us-east-1 METRIST_CLEANUP_ENABLED=1 mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m zoom \
        -s CreateMeeting \
        -s GetMeeting \
        -s DeleteMeeting \
		-e ApiKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#apiKey \
		-e ApiSecret=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#apiSecret \
		-e MeetingUserId=@secret@:@env@:/$(ENVIRONMENT_TAG)/zoom/api-token#meetingUserId
