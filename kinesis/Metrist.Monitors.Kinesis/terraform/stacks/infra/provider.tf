terraform {
  backend "s3" {
    bucket = "cmtf-infra"
    region = "us-west-2"
    // The key is variable, so we set it on `terraform init`
  }
  required_providers {
    aws = {
      source                = "hashicorp/aws"
      version               = "~> 3.0"
      configuration_aliases = [aws.main]
    }
  }
}

provider "aws" {
  region              = var.orchestrator_region
  profile             = "metrist-monitoring"
  allowed_account_ids = ["907343345003"]
}

provider "aws" {
  alias               = "main"
  region              = var.aws_backend_region
  profile             = "default"
  allowed_account_ids = ["123456789"]
}
