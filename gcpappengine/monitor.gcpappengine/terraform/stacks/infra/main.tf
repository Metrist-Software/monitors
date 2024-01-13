locals {
  # Google thinks it's a good idea to have very long region names
  # and very short maximum project names...
  short_region = replace(var.orchestrator_region, "northamerica", "na")
}

# Note: Deploying this requires having access to the billing account used below
resource "google_project" "monitor_project" {
  name       = "mon-appeng-${var.environment_tag}-${local.short_region}"
  project_id = "mon-appeng-${var.environment_tag}-${local.short_region}"
  org_id     = "717524569909" # TODO: Make var?
  billing_account = "01A745-931BB5-737CEE" # TODO: Make var?
}

resource "google_project_service" "app_engine" {
  project = google_project.monitor_project.name
  service = "appengine.googleapis.com"
  disable_dependent_services = true
}

resource "google_project_service" "cloudbuild" {
  project = google_project.monitor_project.name
  service = "cloudbuild.googleapis.com"
  disable_dependent_services = true
}

resource "google_app_engine_application" "monitor_app" {
  project = google_project.monitor_project.project_id
  location_id = local.gae_region
}

resource "google_app_engine_standard_app_version" "app_v1" {
  version_id = "v1"
  service    = "default"
  runtime    = "nodejs18"
  project    = google_project.monitor_project.project_id

  instance_class = "B1"

  entrypoint {
    shell = "node ./app.js"
  }

  deployment {
    zip {
      source_url = local.app_zip_url
    }
  }

  basic_scaling {
    idle_timeout = "10s"
    max_instances = 1
  }

  delete_service_on_destroy = true
}

data "google_app_engine_default_service_account" "default" {
  project = google_project.monitor_project.project_id
  depends_on = [google_app_engine_application.monitor_app]
}

resource "google_service_account_key" "sa_key" {
  service_account_id = data.google_app_engine_default_service_account.default.id
}

resource "aws_secretsmanager_secret" "secret" {
  name = "/${var.environment_tag}/monitors/gcpappengine/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    private_key = google_service_account_key.sa_key.private_key
    project_id  = google_project.monitor_project.project_id
    app_zip_url = local.app_zip_url
    app_hostname = google_app_engine_application.monitor_app.default_hostname
  })
}

# Unfortunately this includes the node_modules folder. No great way of excluding
# it, but shouldn't be too big of an issue either way
data "archive_file" "app" {
  type        = "zip"
  output_path = "${path.root}/../../../app.zip"
  source_dir = "${path.root}/../../../app"
}

resource "google_storage_bucket_object" "app_zip" {
  name = "app-${data.archive_file.app.output_md5}.zip"
  source   = data.archive_file.app.output_path
  bucket   = google_app_engine_application.monitor_app.default_bucket
}

locals {
  app_zip_url = "https://storage.googleapis.com/${google_app_engine_application.monitor_app.default_bucket}/${google_storage_bucket_object.app_zip.name}"
}
