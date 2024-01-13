variable "aws_backend_region" {
  type = string
}

terraform {
  backend "s3" {
    bucket         = "cmtf-infra"
    region         = "us-west-2"
    // The key is variable, so we set it on `terraform init`
  }
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    aws = {
      source  = "hashicorp/aws"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {}
}

provider "aws" {
  region = var.aws_backend_region
}
