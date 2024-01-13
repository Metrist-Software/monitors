
export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m awscloudfront \
		-s PublishFile \
		-s GetNewFile \
		-s UpdateFile \
		-s PurgeFile \
		-s GetUpdatedFile \
		-s DeleteFile \
		-s WaitForDeletionPropagation \
		-e AWSRegion=@env@:$(ORCHESTRATOR_REGION) \
		-e AWSAccessKeyID=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awscloudfront/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		-e AWSSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awscloudfront/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key \
		-e BucketName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awscloudfront/$(ORCHESTRATOR_REGION)/secrets#bucket \
		-e DistributionDomainName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awscloudfront/$(ORCHESTRATOR_REGION)/secrets#distribution_domain_name \
		-e DistributionId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awscloudfront/$(ORCHESTRATOR_REGION)/secrets#distribution_id
