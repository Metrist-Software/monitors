export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js --trace-warnings" \
		-m neon \
		-s CreateBranch \
		-s DeleteBranch \
		-e ApiKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/neon#ApiKey \
		-e ProjectId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/neon#ProjectId \
		-e BaseBranchId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/neon#BaseBranchId
