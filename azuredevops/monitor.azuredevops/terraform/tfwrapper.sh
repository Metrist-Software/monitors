#!/usr/bin/env bash

# Fetches the AZDO_ORG_SERVICE_URL and AZDO_PERSONAL_ACCESS_TOKEN 
# env variables from secrets manager. This will be used by the azuredevops provider
#
# When running this script, make sure to set your aws region using the AWS_DEFAULT_REGION env var

SECRET=$(aws secretsmanager get-secret-value \
   --secret-id /${ENVIRONMENT_TAG}/azuredevops/access-token \
   --query 'SecretString' \
   --output text)


export AZDO_ORG_SERVICE_URL=$(jq '.AZDO_ORG_SERVICE_URL' -r <<< $SECRET)
export AZDO_PERSONAL_ACCESS_TOKEN=$(jq '.AZDO_PERSONAL_ACCESS_TOKEN' -r <<< $SECRET)

terraform "$@"