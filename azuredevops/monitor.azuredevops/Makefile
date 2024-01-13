export CLOUD_PLATFORM ?= az
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js" \
		-m azuredevops \
		-s CloneRepo \
		-s PushCode \
		-s RemoveRemoteBranch \
		-e organization=@secret@:@env@:/$(ENVIRONMENT_TAG)/azuredevops/access-token#ORGANIZATION \
		-e personalAccessToken=@secret@:@env@:/$(ENVIRONMENT_TAG)/azuredevops/access-token#AZDO_PERSONAL_ACCESS_TOKEN \
		-e repository=@secret@:@env@:/$(ENVIRONMENT_TAG)/azuredevops/access-token#REPOSITORY \
		--timeout 300
