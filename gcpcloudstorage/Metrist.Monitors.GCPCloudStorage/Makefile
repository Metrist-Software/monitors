export CLOUD_PLATFORM ?= gcp
include ../../Makefile.common
include ../../Makefile.dotnet
include ../../Makefile.terraform

export ENVIRONMENT_TAG ?= dev1
export AWS_BACKEND_REGION ?= us-east-1
export ORCHESTRATOR_REGION ?= us-east1
export METRIST_CLEANUP_ENABLED ?= 1

test_run: build
	cd ../../../orchestrator; \
	test -f priv/runner/Metrist.Runner || ./install-runner.sh; \
	mix metrist.run_monitor -t rundll -l "$(PWD)/bin/Release/net6.0/linux-x64/publish" \
		-m gcpcloudstorage \
		-s CreateBucket -s UploadObject -s GetObject -s DeleteObject -s DeleteBucket \
		-e PrivateKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/gcpcloudstorage/secrets#private_key \
		-e ProjectId=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/gcpcloudstorage/secrets#project_id
