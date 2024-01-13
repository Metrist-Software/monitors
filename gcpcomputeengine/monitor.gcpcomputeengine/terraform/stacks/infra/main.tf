resource "google_service_account" "sa" {
  account_id   = "${var.env}-gce-monitor"
  display_name = "${var.env} GCE Monitor"
}

resource "google_service_account_key" "sa_key" {
  service_account_id = google_service_account.sa.name
}

resource "google_project_iam_member" "sa_role" {
  project = var.gcp_project_id
  role    = "roles/compute.instanceAdmin.v1"
  member  = "serviceAccount:${google_service_account.sa.email}"

}

resource "aws_secretsmanager_secret" "secret" {
  name = "/${var.env}/monitors/gcpcomputeengine/secrets"
}


resource "aws_secretsmanager_secret_version" "current" {
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    private_key = google_service_account_key.sa_key.private_key
    project_id  = var.gcp_project_id
  })
}
