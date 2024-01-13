include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m fastly \
		-s PurgeCache \
		-s GetNonCachedFile \
		-s GetCachedFile \
		-e RequestUri=@env@:http://fastly-monitor.canarymonitor.com/fastly-$(ENVIRONMENT_TAG)-$(ORCHESTRATOR_REGION).txt
