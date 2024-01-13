
export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m awseks \
		-s CreateDeployment \
		-s RemoveDeployment \
		-e EnvironmentTag=@env@:$(ENVIRONMENT_TAG) \
		-e AWSRegion=@env@:$(ORCHESTRATOR_REGION) \
		-e AWSAccessKeyID=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awseks/$(ORCHESTRATOR_REGION)/secrets#aws_access_key_id \
		-e AWSSecretAccessKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awseks/$(ORCHESTRATOR_REGION)/secrets#aws_secret_access_key \
		-e ClusterName=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awseks/$(ORCHESTRATOR_REGION)/secrets#aws_eks_cluster_name \
		-e ClusterServerAddress=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awseks/$(ORCHESTRATOR_REGION)/secrets#aws_eks_cluster_server_address \
		-e ClusterCertificateAuthorityData=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/awseks/$(ORCHESTRATOR_REGION)/secrets#aws_eks_cluster_certificate_authority_data \
		--timeout 900
