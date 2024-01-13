export CLOUD_PLATFORM ?= aws
include ../../Makefile.common
include ../../Makefile.nodets

test_run: build
	cd ../../../orchestrator; \
	mix metrist.run_monitor -t cmd -l "cd $(PWD); node build/main.js" \
		-m awsrdspersistent \
		-s PingInstance \
		-e dbname=develop \
		-e password=@secret@:/dev1/rds/develop/postgres#password \
		-e user=postgres \
		-e address=@secret@:/dev1/rds/develop/postgres#host  \
		-e engine=postgres \
		-e port=5432 \
		--timeout 30
