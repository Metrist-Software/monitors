
include ../../Makefile.common
include ../../Makefile.dotnet

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t rundll \
		-l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m gke \
		-s CreateDeployment \
		-s RemoveDeployment \
		-e Region=@secret@:@env@:/${ENVIRONMENT_TAG}/monitors/gke#Region \
		-e Base64Keyfile=@secret@:@env@:/${ENVIRONMENT_TAG}/monitors/gke#Base64Keyfile \
		-e ClusterServer=@secret@:@env@:/${ENVIRONMENT_TAG}/monitors/gke#ClusterServer \
		-e ClusterCertAuthData=@secret@:@env@:/${ENVIRONMENT_TAG}/monitors/gke#ClusterCertAuthData
	exit 1
