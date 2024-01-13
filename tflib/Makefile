# Makefile to "do the right thing" with the shared monitoring infrastructure. It reuses environment variables
# we already use elsewhere to keep things simple.
#
# IMPORTANT NOTE: while you can swap "on the fly", this won't work as the S3 path to the state file has the
# environment tag and the AWS region embedded. So if you change from, say, dev to prod, you have to do an
# init in between.
#
# (tbd: make init a prerequisite of plan/apply)
#
.PHONY: plan apply runtf

ENVIRONMENT_TAG ?= dev1
ORCHESTRATOR_REGION ?= us-east-1

plan:
	make ACTION=plan runtf

apply:
	make ACTION=apply runtf

runtf:
	cd stacks/infra; terraform $(ACTION) -var region=$(ORCHESTRATOR_REGION) -var env=$(ENVIRONMENT_TAG)

clean:
	cd stacks/infra; rm -rf .terraform .terraform.lock.hcl

init: clean
	@echo
	@echo Copy/paste this value whewn terraform prompts for key: terraform/monitoring/$(ORCHESTRATOR_REGION)/$(ENVIRONMENT_TAG)/infra.statefile
	@echo
	cd stacks/infra; terraform init
