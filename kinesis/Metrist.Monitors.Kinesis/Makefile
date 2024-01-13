export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m kinesis \
		-s WriteToStream \
		-s ReadFromStream \
		-e Region=@env@:$(ORCHESTRATOR_REGION) \
		-e StreamName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/kinesis/\$(AWS_BACKEND_REGION)/secrets#stream_name \
		-e AwsAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/kinesis/$(AWS_BACKEND_REGION)/secrets#aws_access_key_id \
		-e AwsSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/kinesis/$(AWS_BACKEND_REGION)/secrets#aws_secret_access_key
