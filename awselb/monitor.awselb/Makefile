export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js" \
		-m awselb \
		-s ChangeTargetGroup --timeout 120 \
		-e AWSAccessKeyID=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awselb/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		-e AWSSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awselb/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key \
		-e DnsName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awselb/$(ORCHESTRATOR_REGION)/secrets#aws_elb_dns_name \
		-e TargetGroupArn=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awselb/$(ORCHESTRATOR_REGION)/secrets#aws_elb_target_group_arn \
		-e ServiceId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awselb/$(ORCHESTRATOR_REGION)/secrets#aws_ecs_service_id \
		-e ClusterId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awselb/$(ORCHESTRATOR_REGION)/secrets#aws_ecs_cluster_id
