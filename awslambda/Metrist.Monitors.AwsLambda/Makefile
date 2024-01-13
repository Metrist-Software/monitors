export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m awslambda \
		-s TriggerLambdaAndWaitForResponse \
		-e Region=@env@:$(ORCHESTRATOR_REGION) \
		-e TestFunctionArn=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awslambda/$(ORCHESTRATOR_REGION)/secrets#test_function_arn \
		-e QueueUrl=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awslambda/$(ORCHESTRATOR_REGION)/secrets#queue_url \
		-e AwsAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awslambda/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		-e AwsSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awslambda/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key
