
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
        mix metrist.run_monitor -t rundll \
			-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
			-m asana \
			-s Ping \
			-s CreateTask \
			-s GetTask \
			-s DeleteTask \
			-e PersonalAccessToken=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/asana/secrets#personal_access_token \
			-e ProjectId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/asana/secrets#project_id \
			-e WorkspaceId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/asana/secrets#workspace_id
