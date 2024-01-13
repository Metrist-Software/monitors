include ../../Makefile.common
include ../../Makefile.nodets

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd \
		-l "cd $(PWD); node build/main.js --trace-warnings" \
		-m googledrive \
		-s CreateDocsFile \
		-s GetDocsFile \
		-s DeleteDocsFile \
		-e GoogleDriveAccountPrivateKey=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/googledrive/$(ORCHESTRATOR_REGION)/secrets#google_drive_private_key \
		-e GoogleDriveAccountEmail=@secret@:@env@:/$(ENVIRONMENT_TAG)/monitors/googledrive/$(ORCHESTRATOR_REGION)/secrets#google_drive_service_email
