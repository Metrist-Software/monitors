include ../../Makefile.common
include ../../Makefile.dotnet

# Helper target to do a test run. Assumes Orchestrator is checked out next to us.
test_run: build
	test -n "$$HUBSPOT_APP_TOKEN" || ( echo "Please set HUBSPOT_APP_TOKEN"; exit 1 )

	cd ../../../orchestrator; \
	  test -f priv/runner/Metrist.Runner || ./install-runner.sh; \
	  mix metrist.run_monitor -t rundll \
			-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
			-m hubspot \
			-s GetContacts \
			--timeout 120 \
			--extra-config ApiKey=$(HUBSPOT_APP_TOKEN)
