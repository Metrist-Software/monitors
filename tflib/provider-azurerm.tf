variable "aws_backend_region" {
  type = string
}

variable "environment_tag" {
  type = string
}

variable "platform" {
  type = string
}

variable "orchestrator_region" {
  type = string
}

data "aws_secretsmanager_secret" "azurerm_secret" {
  # This one isn't per region but is global so this does not need updating
  name = "/${var.environment_tag}/azure/api-token"
}

data "aws_secretsmanager_secret_version" "azurerm_current" {
  secret_id = data.aws_secretsmanager_secret.azurerm_secret.id
}

# For use in Azure-friendly names (24 characters max, letters only)
# With env/platform being 6 characters and resources appending up to an additional
# 12 characters, these can be at most 6 characters long
variable "short_envs" {
  type = map(string)
  default = {
    "eastus"         = "eastu"
    "eastus2"        = "eastu2"
    "centralus"      = "cntru"
    "southcentralus" = "scntru"
    "westus"         = "westu"
    "westus2"        = "westu2"
    "canadacentral"  = "cacntr"
  }
}

locals {
  azure_secret = jsondecode(data.aws_secretsmanager_secret_version.azurerm_current.secret_string)
  short_env = "${var.environment_tag}${var.platform}${lookup(var.short_envs, var.orchestrator_region)}"
}

terraform {
  backend "s3" {
    bucket         = "cmtf-infra"
    region         = "us-west-2"
    // The key is variable, so we set it on `terraform init`
  }
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 3.0"
    }
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "=3.5.0"
    }
  }
}

provider "aws" {
  region = var.aws_backend_region
}

provider "azurerm" {
  features {}
  subscription_id = local.azure_secret["subscription-id"]
  client_id       = local.azure_secret["client-id"]
  client_secret   = local.azure_secret["client-secret"]
  tenant_id       = local.azure_secret["tenant-id"]
}
