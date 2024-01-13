terraform {
  backend "s3" {
    bucket         = "cmtf-infra"
    region         = "us-west-2"
    // The key is variable, so we set it on `terraform init`
  }

  required_providers {
    azuredevops = {
      source  = "microsoft/azuredevops"
      version = ">=0.1.0"
    }
  }
}
