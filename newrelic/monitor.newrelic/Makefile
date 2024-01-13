export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets

test_run:  build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js --trace-warnings" \
		-m newrelic \
		-s SubmitEvent \
		-s CheckEvent \
		-s SubmitMetric \
		-e NewRelicAccountNumber=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/newrelic/$(ORCHESTRATOR_REGION)/secrets#new_relic_account_number \
		-e NewRelicInsightAPIKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/newrelic/$(ORCHESTRATOR_REGION)/secrets#new_relic_insight_api_key \
		-e NewRelicNerdGraphUSERKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/newrelic/$(ORCHESTRATOR_REGION)/secrets#new_relic_nerdgraph_user_key

test_run_synthetic_monitor: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js --trace-warnings" \
		-m newrelic \
		-s CreateSyntheticMonitor \
		-s WaitForSyntheticMonitorResponse \
		-s DeleteSyntheticMonitor  \
		-e NewRelicAccountNumber=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/newrelic/$(ORCHESTRATOR_REGION)/secrets#new_relic_account_number \
		-e NewRelicInsightAPIKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/newrelic/$(ORCHESTRATOR_REGION)/secrets#new_relic_insight_api_key \
		-e NewRelicNerdGraphUSERKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/newrelic/$(ORCHESTRATOR_REGION)/secrets#new_relic_nerdgraph_user_key
