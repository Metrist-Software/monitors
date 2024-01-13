
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m trello \
		-s CreateCard \
		-s DeleteCard \
		-e AppKey=@secret@:/$(ENVIRONMENT_TAG)/monitors/trello#AppKey \
		-e UserToken=@secret@:/$(ENVIRONMENT_TAG)/monitors/trello#UserToken \
		-e ListId=@secret@:/$(ENVIRONMENT_TAG)/monitors/trello#ListId
