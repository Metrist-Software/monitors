include ../../Makefile.common
include ../../Makefile.nodets

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js --trace-warnings" \
		-m envoy \
		-s GetEmployees \
		-s GetReservations \
		-e clientAPIKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/envoy/$(AWS_BACKEND_REGION)/secrets#api_key
