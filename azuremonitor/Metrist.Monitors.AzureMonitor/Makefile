export CLOUD_PLATFORM ?= az
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish"\
		-m azuremonitor \
		-e ConnectionString=@secret@:/$(ENVIRONMENT_TAG)/az/$(ORCHESTRATOR_REGION)/azuremonitor/credentials#ConnectionString \
		-s TrackEvent \
		-s TrackMetricValue \
		-s TrackExc \
		-s TrackTrace \
		-s SendLog
