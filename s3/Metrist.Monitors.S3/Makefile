export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform


test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m s3 \
		-s PutBucket \
		-s PutObject \
		-s GetObject \
		-s DeleteObject \
		-s DeleteBucket \
		-e Region=@env@:$(AWS_BACKEND_REGION) \
		-e AwsAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/s3/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		-e AwsSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/s3/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key
