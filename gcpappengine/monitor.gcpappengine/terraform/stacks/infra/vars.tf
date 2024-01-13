variable "aws_backend_region" {
  type = string
}

variable "environment_tag" {
  type = string
}

variable "orchestrator_region" {
  type = string
}

locals {
  _gae_region_override =  {
    "us-central1" = "us-central"
  }

  # We use this local variable to translate GCP region to GAE region
  #
  #   Note: Two locations, which are called europe-west and us-central in App Engine commands
  #   and in the Google Cloud console, are called europe-west1 and us-central1,
  #   respectively, elsewhere in Google documentation.
  gae_region = lookup(local._gae_region_override, var.orchestrator_region, var.orchestrator_region)
}
