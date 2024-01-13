
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	  test -f priv/runner/Metrist.Runner || ./install-runner.sh;\
	  mix metrist.run_monitor -t rundll \
			-l "$(PWD)/bin/Release/net6.0/linux-x64/publish"\
			-m cloudflare \
			-s Ping \
			-s DNSLookup \
			-s CDN \
			--timeout 120 \
			--extra-config CdnCheckUrl=https://www.cloudflare.com
