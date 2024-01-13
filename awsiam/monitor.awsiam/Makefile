export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js --trace-warnings" \
		-m awsiam \
		-s CreateUser \
		-s CreateGroup \
		-s AddUserToGroup \
		-s RemoveUserFromGroup \
		-s DeleteGroup \
		-s AttachPolicy \
		-s DetachPolicy \
		-s DeleteUser \
		-e AWSAccessKeyId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsiam/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		-e AWSSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awsiam/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key \
