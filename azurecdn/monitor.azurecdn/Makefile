CLOUD_PLATFORM ?= az
include ../../Makefile.common
include ../../Makefile.nodets
include ../../Makefile.terraform

# Helper target to do a test run. Assumes Orchestrator is checked out next to us.v
test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js" \
		-m azurecdn \
		-s GetLongCachedFile \
		-s GetNewFile \
		-s PurgeFile \
		-s UpdateFile \
		-s DeleteFile \
		-e BlobStorageConnectionString=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(CLOUD_PLATFORM)/\$(ORCHESTRATOR_REGION)/azurecdn/credentials#blob-storage-connection-string \
		-e BlobStorageContainerName=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(CLOUD_PLATFORM)/\$(ORCHESTRATOR_REGION)/azurecdn/credentials#blob-storage-container-name \
		-e CacheFileName=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(CLOUD_PLATFORM)/\$(ORCHESTRATOR_REGION)/azurecdn/credentials#cache-file-name \
		-e CacheFilePath=/content/cachefile.txt \
		-e CdnEndpointName=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(CLOUD_PLATFORM)/\$(ORCHESTRATOR_REGION)/azurecdn/credentials#cdn-endpoint-name \
		-e CdnProfileName=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(CLOUD_PLATFORM)/\$(ORCHESTRATOR_REGION)/azurecdn/credentials#cdn-profile-name \
		-e ClientID=@env@:@secret@:/$(ENVIRONMENT_TAG)/azure/api-token#client-id \
		-e ClientSecret=@env@:@secret@:/$(ENVIRONMENT_TAG)/azure/api-token#client-secret \
		-e PurgeFilePath=/content/purgefile.txt \
		-e ResourceGroupName=@secret@:@env@:/$(ENVIRONMENT_TAG)/\$(CLOUD_PLATFORM)/\$(ORCHESTRATOR_REGION)/azurecdn/credentials#resource-group-name \
		-e SubscriptionID=@env@:@secret@:/$(ENVIRONMENT_TAG)/azure/api-token#subscription-id \
		-e TenantID=@env@:@secret@:/$(ENVIRONMENT_TAG)/azure/api-token#tenant-id \
		--timeout 300
