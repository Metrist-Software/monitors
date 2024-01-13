export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js" \
		-m awsrds \
		-s CreateInstance \
		-s PingInstance \
		-s DestroyInstance \
		-e Region=@env@:$(AWS_BACKEND_REGION) \
		-e EnvironmentTag=@env@:$(ENVIRONMENT_TAG) \
		-e SecurityGroupId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsrds/$(ORCHESTRATOR_REGION)/secrets#vpc_security_group_id \
		-e SubnetGroupName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsrds/$(ORCHESTRATOR_REGION)/secrets#db_subnet_group_name \
		-e AwsAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsrds/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		-e AwsSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsrds/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key \
		--timeout 900
