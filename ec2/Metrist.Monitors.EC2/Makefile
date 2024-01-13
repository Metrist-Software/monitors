export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish"\
		-m ec2\
		-e Region=@env@:$(ORCHESTRATOR_REGION)\
		-e AmiID=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/ec2/$(AWS_BACKEND_REGION)/secrets#ami_id\
		-e PersistentInstanceId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/ec2/$(AWS_BACKEND_REGION)/secrets#persistent_instance_id\
		-e AwsAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/ec2/$(AWS_BACKEND_REGION)/secrets#aws_access_key_id\
		-e AwsSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/ec2/$(AWS_BACKEND_REGION)/secrets#aws_secret_access_key\
		-s RunInstance\
		-s TerminateInstance\
		-s DescribePersistentInstance
