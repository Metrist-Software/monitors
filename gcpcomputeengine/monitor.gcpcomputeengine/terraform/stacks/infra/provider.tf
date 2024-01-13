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
  }
}

provider "aws" {
  region = var.region
}

provider "google" {
  project = var.gcp_project_id
}