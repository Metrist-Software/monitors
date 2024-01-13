terraform {
  backend "s3" {
    bucket = "cmtf-infra"
    region = "us-west-2"
    // The key is variable, so we set it on `terraform init`
  }
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 3.0"
    }
  }
}


provider "aws" {
  profile             = "metrist-monitoring"
  region              = var.orchestrator_region
  allowed_account_ids = ["907343345003"]
}

provider "aws" {
  alias               = "main"
  region              = var.aws_backend_region
  profile             = "default"
  allowed_account_ids = ["123456789"]
}
