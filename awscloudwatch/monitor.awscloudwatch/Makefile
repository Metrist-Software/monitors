
export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js" \
		-m awscloudwatch \
		-s SubmitEvent -s GetEvent \
		--extra-config AWSAccessKeyID=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awscloudwatch/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		--extra-config AWSSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awscloudwatch/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key
