export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-t "rundll" \
		-m cognito \
		-s CreateUser \
		-s DeleteUser \
		-e AwsAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(ORCHESTRATOR_REGION)/monitors/cognito/secrets#aws_access_key_id \
		-e AwsSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(ORCHESTRATOR_REGION)/monitors/cognito/secrets#aws_secret_access_key \
		-e UserPool=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(ORCHESTRATOR_REGION)/monitors/cognito/secrets#user_pool
