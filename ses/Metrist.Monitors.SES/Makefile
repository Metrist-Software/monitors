export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m ses \
		-s SendEmail \
		-e FromEmail=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/ses/$ORCHESTRATOR_REGION)/secrets#from_email \
		-e ToEmail=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/ses/$(ORCHESTRATOR_REGION)/secrets#to_email \
		-e AwsAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/ses/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		-e AwsSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/ses/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key
