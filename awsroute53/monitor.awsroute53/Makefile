export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js" \
		-m awsroute53 \
		-s QueryExistingDNSRecord -s QueryExistingDNSRecordAPI -s CreateDNSRecord -s RemoveDNSRecord --timeout 120 \
		--extra-config AWSAccessKeyID=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsroute53/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		--extra-config AWSSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsroute53/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key \
		--extra-config PersistentRecordName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsroute53/$(ORCHESTRATOR_REGION)/secrets#persistent_record_name \
		--extra-config HostedZoneID=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsroute53/$(ORCHESTRATOR_REGION)/secrets#hosted_zone_id \
		--extra-config HostedZoneNS=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsroute53/$(ORCHESTRATOR_REGION)/secrets#hosted_zone_ns \
		--extra-config HostedZoneName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsroute53/$(ORCHESTRATOR_REGION)/secrets#hosted_zone_name
