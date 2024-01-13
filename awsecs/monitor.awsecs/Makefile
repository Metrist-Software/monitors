export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js --trace-warnings" \
	  -s CreateService \
	  -s PingService \
	  -s DestroyService \
	  -e Region=@env@:$(ORCHESTRATOR_REGION) \
	  -e EnvironmentTag=@env@:$(ENVIRONMENT_TAG) \
	  -e AwsAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsecs/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
	  -e AwsSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsecs/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key \
	  -e SecurityGroupId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsecs/$(ORCHESTRATOR_REGION)/secrets#vpc_security_group_id \
	  -e ClusterId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsecs/$(ORCHESTRATOR_REGION)/secrets#aws_ecs_cluster_id \
	  -e VpcPublicSubnets=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsecs/$(ORCHESTRATOR_REGION)/secrets#aws_ecs_vpc_public_subnets \
	  -e AwsLbTargetGroupArn=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsecs/$(ORCHESTRATOR_REGION)/secrets#aws_ecs_lb_target_group_arn \
	  -e AwsTaskDefinitionArn=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsecs/$(ORCHESTRATOR_REGION)/secrets#aws_ecs_task_definition_arn \
	  -e AwsLbDnsName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsecs/$(ORCHESTRATOR_REGION)/secrets#aws_ecs_lb_dns_name \
	  --timeout 300
