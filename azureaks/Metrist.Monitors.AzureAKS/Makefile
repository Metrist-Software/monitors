export CLOUD_PLATFORM ?= az
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish"\
		-m azureaks \
		-e ClientID=@secret@:/$(ENVIRONMENT_TAG)/azure/api-token#client-id \
		-e ClientSecret=@secret@:/$(ENVIRONMENT_TAG)/azure/api-token#client-secret \
		-e Region=$(AWS_BACKEND_REGION) \
		-e SubscriptionID=@secret@:/$(ENVIRONMENT_TAG)/azure/api-token#subscription-id \
		-e TenantID=@secret@:/$(ENVIRONMENT_TAG)/azure/api-token#tenant-id \
		-s CreateCluster \
		-s CreateDeployment \
		-s RemoveDeployment
