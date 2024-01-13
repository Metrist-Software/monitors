include ../../Makefile.common
include ../../Makefile.dotnet

# Helper target to do a test run. Assumes Orchestrator is checked out next to us.
test_run: build
	cd ../../../orchestrator; \
	  test -f priv/runner/Metrist.Runner || ./install-runner.sh; \
	  mix metrist.run_monitor -t rundll \
			-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
			-m slack \
			-s PostMessage \
			-s ReadMessage \
			--timeout 120 \
			--extra-config ApiToken=$(SLACK_API_TOKEN) \
			--extra-config Channel=#mon-slack-heartbeat \
			--extra-config Tag=$(ENVIRONMENT_TAG)
