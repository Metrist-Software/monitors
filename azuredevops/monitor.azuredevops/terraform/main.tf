resource "azuredevops_project" "project" {
  name               = "Monitor"
  visibility         = "private"
  version_control    = "Git"

  features = {
    "testplans"    = "enabled"
    "artifacts"    = "enabled"
    "repositories" = "enabled"
    "boards"       = "enabled"
  }
}
